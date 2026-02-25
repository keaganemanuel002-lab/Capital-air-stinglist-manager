using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class FirestoreDataStore : IDataStore
{
    private readonly AppSettings _settings;
    private readonly LocalSqliteDataStore _local = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _billingSyncLock = new(1, 1);
    private long _jobCardMirrorInFlight;
    private DateTime _lastJobCardMirrorUtc = DateTime.MinValue;
    private DateTime _lastBillingSyncUtc = DateTime.MinValue;
    private static readonly TimeSpan JobCardMirrorInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BillingSyncInterval = TimeSpan.FromSeconds(15);

    private FirestoreDb? _firestore;
    private bool _initAttempted;
    private string? _initError;

    private sealed class BillingSyncRow
    {
        public string DocId { get; init; } = string.Empty;
        public int Id { get; init; }
        public string Company { get; init; } = string.Empty;
        public string Registration { get; init; } = string.Empty;
        public string? FleetNumber { get; init; }
        public string? Make { get; init; }
        public string? Model { get; init; }
        public string? Colour { get; init; }
        public string? VinNumber { get; init; }
        public string? TrackingUnitMake { get; init; }
        public string? StingPackageType { get; init; }
        public string? Notes { get; init; }
        public string? Reason { get; init; }
        public string? Imei { get; init; }
        public string? SerialNumber { get; init; }
        public string? Iccid { get; init; }
        public string? SimNumber { get; init; }
        public BillingStatus Status { get; init; }
        public DateTime ActiveFrom { get; init; }
        public DateTime? ActiveTo { get; init; }
        public DateTime? ArchivedAt { get; init; }
        public string RegistrationNorm { get; init; } = string.Empty;
        public string ImeiNorm { get; init; } = string.Empty;
        public string IccidNorm { get; init; } = string.Empty;
        public string SerialNumberNorm { get; init; } = string.Empty;
        public DateTime RemoteUpdatedAtUtc { get; init; } = DateTime.UnixEpoch;
    }

    public FirestoreDataStore(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<List<Client>> GetClientsAsync(
        string? searchText,
        HashSet<string>? activeWialonClientKeys,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return await _local.GetClientsAsync(searchText, activeWialonClientKeys, cancellationToken);

        try
        {
            await MirrorClientsFromLocalAsync(cancellationToken);

            var snapshot = await _firestore!.Collection(FirestoreCollections.Clients).GetSnapshotAsync(cancellationToken);
            var rows = snapshot.Documents
                .Select(MapClientDocument)
                .Where(c => c != null)
                .Cast<Client>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var s = searchText.Trim();
                rows = rows.Where(c => c.Name.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (activeWialonClientKeys is not null)
            {
                rows = rows
                    .Where(c => activeWialonClientKeys.Contains(NormalizeComparableText(c.Name)))
                    .ToList();
            }

            if (rows.Count == 0)
                return await _local.GetClientsAsync(searchText, activeWialonClientKeys, cancellationToken);

            return rows.OrderBy(c => c.Name).ToList();
        }
        catch
        {
            return await _local.GetClientsAsync(searchText, activeWialonClientKeys, cancellationToken);
        }
    }

    public async Task<ClientSaveResult> SaveClientAsync(
        int? selectedClientId,
        string name,
        string? contactPerson,
        string? phoneNumber,
        string? emailAddress,
        string? address,
        CancellationToken cancellationToken = default)
    {
        var localResult = await _local.SaveClientAsync(
            selectedClientId,
            name,
            contactPerson,
            phoneNumber,
            emailAddress,
            address,
            cancellationToken);

        if (!localResult.Success || localResult.Client is null)
            return localResult;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return localResult;

        try
        {
            await UpsertClientDocumentAsync(localResult.Client, cancellationToken);
        }
        catch
        {
            // Local save already succeeded.
        }

        return localResult;
    }

    public async Task<bool> DeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var deleted = await _local.DeleteClientAsync(clientId, cancellationToken);
        if (!deleted)
            return false;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return true;

        try
        {
            await _firestore!.Collection(FirestoreCollections.Clients).Document(clientId.ToString()).DeleteAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            // Local delete already succeeded.
        }

        return true;
    }

    public async Task<int> InsertMissingClientsAsync(IEnumerable<string> clientNames, CancellationToken cancellationToken = default)
    {
        var inserted = await _local.InsertMissingClientsAsync(clientNames, cancellationToken);
        if (inserted <= 0)
            return 0;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return inserted;

        try
        {
            await MirrorClientsFromLocalAsync(cancellationToken);
        }
        catch
        {
            // Local insert already succeeded.
        }

        return inserted;
    }

    public async Task<List<JobCardListItem>> GetJobCardsAsync(JobCardQuery query, CancellationToken cancellationToken = default)
    {
        // Local DB remains the fastest and most up-to-date source for desktop UI interactions.
        var localRows = await _local.GetJobCardsAsync(query, cancellationToken);
        QueueJobCardsMirror();
        return localRows;
    }

    public Task<int> CountJobCardPhotosAsync(int jobCardId, CancellationToken cancellationToken = default)
    {
        // Completion workflow still relies on local attachment files in phase 1.
        return _local.CountJobCardPhotosAsync(jobCardId, cancellationToken);
    }

    public async Task<QuotePageResult> GetQuotesAsync(QuoteQuery query, CancellationToken cancellationToken = default)
    {
        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return await _local.GetQuotesAsync(query, cancellationToken);

        try
        {
            await MirrorQuotesFromLocalAsync(cancellationToken);

            var snapshot = await _firestore!.Collection(FirestoreCollections.Quotes).GetSnapshotAsync(cancellationToken);
            var rows = snapshot.Documents
                .Select(MapQuoteDocument)
                .Where(r => r != null)
                .Cast<QuoteListItem>()
                .ToList();

            if (rows.Count == 0)
                return await _local.GetQuotesAsync(query, cancellationToken);

            return ApplyQuoteFiltersAndPaging(rows, query);
        }
        catch
        {
            return await _local.GetQuotesAsync(query, cancellationToken);
        }
    }

    public async Task<QuoteApproveResult> ApproveQuoteAsync(
        int quoteId,
        string actor,
        DateTime? scheduleDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _local.ApproveQuoteAsync(quoteId, actor, scheduleDate, cancellationToken);
        if (!result.Success)
            return result;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return result;

        try
        {
            await MirrorQuotesFromLocalAsync(cancellationToken);
            await MirrorJobCardsFromLocalAsync(cancellationToken);
        }
        catch
        {
            // Local operation already succeeded.
        }

        return result;
    }

    public async Task<QuoteCancelResult> CancelDraftQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        var result = await _local.CancelDraftQuoteAsync(quoteId, cancellationToken);
        if (!result.Success)
            return result;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return result;

        try
        {
            await MirrorQuotesFromLocalAsync(cancellationToken);
        }
        catch
        {
            // Local operation already succeeded.
        }

        return result;
    }

    public async Task<QuoteDeleteResult> DeleteQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        var relatedJobCards = await _local.GetJobCardsAsync(new JobCardQuery
        {
            SelectedStatus = "All",
            SelectedType = "All",
            CompanyFilter = null,
            RegistrationFilter = null,
            StartDate = null,
            EndDate = null,
            QuoteIdFilter = quoteId
        }, cancellationToken);

        var result = await _local.DeleteQuoteAsync(quoteId, cancellationToken);
        if (!result.Success)
            return result;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return result;

        try
        {
            await _firestore!.Collection(FirestoreCollections.Quotes)
                .Document(quoteId.ToString())
                .DeleteAsync(cancellationToken: cancellationToken);

            foreach (var jobCard in relatedJobCards)
            {
                await _firestore.Collection(FirestoreCollections.JobCards)
                    .Document(jobCard.Id.ToString())
                    .DeleteAsync(cancellationToken: cancellationToken);
            }
        }
        catch
        {
            // Best effort. Local source of truth is already updated.
        }

        return result;
    }

    public Task<bool> HasRelatedJobCardsForQuoteAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        // Local relational view remains source of truth in phase 1.
        return _local.HasRelatedJobCardsForQuoteAsync(quoteId, cancellationToken);
    }

    public Task<Quote?> GetQuoteWithLineItemsAsync(int quoteId, CancellationToken cancellationToken = default)
    {
        // PDF generation still uses full local quote model including line items.
        return _local.GetQuoteWithLineItemsAsync(quoteId, cancellationToken);
    }

    public Task<DateTime?> GetJobCardScheduledForAsync(int jobCardId, CancellationToken cancellationToken = default)
    {
        // Local source remains authoritative in phase 1.
        return _local.GetJobCardScheduledForAsync(jobCardId, cancellationToken);
    }

    public async Task<bool> UpdateJobCardScheduleAsync(int jobCardId, DateTime? scheduledFor, CancellationToken cancellationToken = default)
    {
        var ok = await _local.UpdateJobCardScheduleAsync(jobCardId, scheduledFor, cancellationToken);
        if (!ok)
            return false;

        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return true;

        try
        {
            await MirrorJobCardsFromLocalAsync(cancellationToken);
        }
        catch
        {
            // Local operation already succeeded.
        }

        return true;
    }

    public async Task<List<BillingEntry>> GetActiveBillingEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return await _local.GetActiveBillingEntriesAsync(cancellationToken);

        try
        {
            await EnsureBillingSyncedAsync(cancellationToken);
            return await _local.GetActiveBillingEntriesAsync(cancellationToken);
        }
        catch
        {
            return await _local.GetActiveBillingEntriesAsync(cancellationToken);
        }
    }

    public async Task<BillingEntry?> GetBillingEntryByIdAsync(int billingEntryId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return await _local.GetBillingEntryByIdAsync(billingEntryId, cancellationToken);

        try
        {
            await EnsureBillingSyncedAsync(cancellationToken);
            return await _local.GetBillingEntryByIdAsync(billingEntryId, cancellationToken);
        }
        catch
        {
            return await _local.GetBillingEntryByIdAsync(billingEntryId, cancellationToken);
        }
    }

    public async Task<BillingEntrySaveResult> SaveBillingEntryAsync(
        int? billingEntryId,
        BillingEntrySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureFirestoreReadyAsync(cancellationToken))
            return await _local.SaveBillingEntryAsync(billingEntryId, request, cancellationToken);

        BillingEntry? beforeSave = null;
        BillingEntrySaveResult? localResult = null;

        try
        {
            await EnsureBillingSyncedAsync(cancellationToken, force: true);
            if (billingEntryId is int existingId)
                beforeSave = await _local.GetBillingEntryByIdAsync(existingId, cancellationToken);

            localResult = await _local.SaveBillingEntryAsync(billingEntryId, request, cancellationToken);
            if (!localResult.Success || localResult.Entry is null)
                return localResult;

            var collection = _firestore!.Collection(FirestoreCollections.BillingEntries);
            var syncRow = MapBillingEntityToSyncRow(localResult.Entry);
            var payload = BuildBillingPayload(syncRow, Timestamp.FromDateTime(DateTime.UtcNow));
            await collection.Document(syncRow.DocId).SetAsync(payload, SetOptions.MergeAll, cancellationToken);

            if (beforeSave is not null)
            {
                var oldDocId = BuildBillingDocId(beforeSave);
                if (!string.Equals(oldDocId, syncRow.DocId, StringComparison.Ordinal))
                {
                    try
                    {
                        await collection.Document(oldDocId).DeleteAsync(cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        // Best-effort cleanup when key fields changed.
                    }
                }
            }

            await EnsureBillingSyncedAsync(cancellationToken, force: true);
            return localResult;
        }
        catch (Exception ex)
        {
            if (localResult is { Success: true })
            {
                return new BillingEntrySaveResult
                {
                    Success = true,
                    Message = $"Saved, but Firestore update is pending: {ex.Message}",
                    Entry = localResult.Entry
                };
            }

            return new BillingEntrySaveResult
            {
                Success = false,
                Message = $"Firestore save failed: {ex.Message}"
            };
        }
    }

    private async Task MirrorClientsFromLocalAsync(CancellationToken cancellationToken)
    {
        var localRows = await _local.GetClientsSnapshotAsync(cancellationToken);
        var collection = _firestore!.Collection(FirestoreCollections.Clients);
        var syncedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        foreach (var row in localRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = collection.Document(row.Id.ToString());
            var payload = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["name"] = row.Name,
                ["nameNorm"] = row.NameNorm,
                ["contactPerson"] = row.ContactPerson,
                ["phoneNumber"] = row.PhoneNumber,
                ["emailAddress"] = row.EmailAddress,
                ["address"] = row.Address,
                ["createdAtUtc"] = Timestamp.FromDateTime(EnsureUtc(row.CreatedAt)),
                ["syncedAtUtc"] = syncedAt
            };

            await doc.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
        }
    }

    private async Task MirrorJobCardsFromLocalAsync(CancellationToken cancellationToken)
    {
        var localRows = await _local.GetJobCardsSnapshotAsync(cancellationToken);
        var collection = _firestore!.Collection(FirestoreCollections.JobCards);
        var syncedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        foreach (var row in localRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = collection.Document(row.Id.ToString());
            var payload = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["jobCardNumber"] = row.JobCardNumber,
                ["jobCardReference"] = row.JobCardReference,
                ["quoteId"] = row.QuoteId,
                ["quoteReference"] = row.QuoteReference,
                ["type"] = row.Type,
                ["status"] = row.Status,
                ["photoCount"] = row.PhotoCount,
                ["lastPhotoAtUtc"] = row.LastPhotoAt.HasValue ? Timestamp.FromDateTime(EnsureUtc(row.LastPhotoAt.Value)) : null,
                ["company"] = row.Company,
                ["registration"] = row.Registration,
                ["make"] = row.Make,
                ["model"] = row.Model,
                ["imei"] = row.Imei,
                ["serialNumber"] = row.SerialNumber,
                ["iccid"] = row.Iccid,
                ["scheduledForUtc"] = row.ScheduledFor.HasValue ? Timestamp.FromDateTime(EnsureUtc(row.ScheduledFor.Value)) : null,
                ["createdAtUtc"] = Timestamp.FromDateTime(EnsureUtc(row.CreatedAt)),
                ["syncedAtUtc"] = syncedAt
            };

            await doc.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
        }
    }

    private void QueueJobCardsMirror()
    {
        if (Interlocked.Exchange(ref _jobCardMirrorInFlight, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (DateTime.UtcNow - _lastJobCardMirrorUtc < JobCardMirrorInterval)
                    return;

                if (!await EnsureFirestoreReadyAsync(CancellationToken.None))
                    return;

                await MirrorJobCardsFromLocalAsync(CancellationToken.None);
                _lastJobCardMirrorUtc = DateTime.UtcNow;
            }
            catch
            {
                // Keep desktop responsive; background mirror is best effort.
            }
            finally
            {
                Interlocked.Exchange(ref _jobCardMirrorInFlight, 0);
            }
        });
    }

    private async Task MirrorQuotesFromLocalAsync(CancellationToken cancellationToken)
    {
        var localPage = await _local.GetQuotesAsync(new QuoteQuery
        {
            SelectedStatus = "All",
            SelectedType = "All",
            CompanyFilter = null,
            RegistrationFilter = null,
            StartDate = null,
            EndDate = null,
            PageNumber = 1,
            PageSize = int.MaxValue
        }, cancellationToken);

        var collection = _firestore!.Collection(FirestoreCollections.Quotes);
        var syncedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        foreach (var row in localPage.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = collection.Document(row.Id.ToString());
            var payload = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["quoteNumber"] = row.QuoteNumber,
                ["type"] = row.Type,
                ["status"] = row.Status,
                ["company"] = row.Company,
                ["registration"] = row.Registration,
                ["productType"] = row.ProductType,
                ["amountExVat"] = row.AmountExVat,
                ["createdAtUtc"] = Timestamp.FromDateTime(EnsureUtc(row.CreatedAt)),
                ["syncedAtUtc"] = syncedAt
            };

            await doc.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
        }
    }

    private async Task UpsertClientDocumentAsync(Client client, CancellationToken cancellationToken)
    {
        var doc = _firestore!.Collection(FirestoreCollections.Clients).Document(client.Id.ToString());
        var payload = new Dictionary<string, object?>
        {
            ["id"] = client.Id,
            ["name"] = client.Name,
            ["nameNorm"] = client.NameNorm,
            ["contactPerson"] = client.ContactPerson,
            ["phoneNumber"] = client.PhoneNumber,
            ["emailAddress"] = client.EmailAddress,
            ["address"] = client.Address,
            ["createdAtUtc"] = Timestamp.FromDateTime(EnsureUtc(client.CreatedAt)),
            ["syncedAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await doc.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
    }

    private async Task EnsureBillingSyncedAsync(CancellationToken cancellationToken, bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastBillingSyncUtc < BillingSyncInterval)
            return;

        await _billingSyncLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && DateTime.UtcNow - _lastBillingSyncUtc < BillingSyncInterval)
                return;

            await SyncBillingFromFirestoreToLocalAsync(cancellationToken);
            _lastBillingSyncUtc = DateTime.UtcNow;
        }
        finally
        {
            _billingSyncLock.Release();
        }
    }

    private async Task SyncBillingFromFirestoreToLocalAsync(CancellationToken cancellationToken)
    {
        var collection = _firestore!.Collection(FirestoreCollections.BillingEntries);
        var snapshot = await collection.GetSnapshotAsync(cancellationToken);
        if (snapshot.Count == 0)
        {
            await BootstrapBillingFromLocalAsync(collection, cancellationToken);
            return;
        }

        var remoteRows = snapshot.Documents
            .Select(MapBillingDocumentToSyncRow)
            .Where(x => x is not null)
            .Cast<BillingSyncRow>()
            .OrderBy(x => x.RemoteUpdatedAtUtc)
            .ThenBy(x => x.DocId, StringComparer.Ordinal)
            .ToList();

        if (remoteRows.Count == 0)
            return;

        await ApplyRemoteBillingRowsToLocalAsync(remoteRows, cancellationToken);
    }

    private async Task BootstrapBillingFromLocalAsync(CollectionReference collection, CancellationToken cancellationToken)
    {
        var localRows = await _local.GetBillingEntriesSnapshotAsync(cancellationToken);
        if (localRows.Count == 0)
            return;

        var syncedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        foreach (var localRow in localRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = MapBillingEntityToSyncRow(localRow);
            var payload = BuildBillingPayload(row, syncedAt);
            await collection.Document(row.DocId).SetAsync(payload, SetOptions.MergeAll, cancellationToken);
        }
    }

    private static async Task ApplyRemoteBillingRowsToLocalAsync(
        IReadOnlyCollection<BillingSyncRow> remoteRows,
        CancellationToken cancellationToken)
    {
        using var db = new AppDbContext();
        var localRows = db.BillingEntries.ToList();

        foreach (var remoteRow in remoteRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localMatch = FindLocalBillingEntry(localRows, remoteRow);
            var isNew = localMatch is null;
            if (isNew)
            {
                localMatch = new BillingEntry();
                db.BillingEntries.Add(localMatch);
            }

            ApplyBillingSyncRow(localMatch!, remoteRow);

            try
            {
                var changed = await db.SaveChangesAsync(cancellationToken);
                if (changed > 0 && isNew)
                    localRows.Add(localMatch!);
            }
            catch (DbUpdateException)
            {
                if (isNew)
                {
                    db.Entry(localMatch!).State = EntityState.Detached;
                }
                else
                {
                    await db.Entry(localMatch!).ReloadAsync(cancellationToken);
                }
            }
        }
    }

    private static BillingSyncRow MapBillingEntityToSyncRow(BillingEntry entry)
    {
        var company = NormalizeDisplayText(entry.Company) ?? string.Empty;
        var registration = NormalizeBillingComparable(entry.Registration);
        var activeFrom = entry.ActiveFrom == default ? DateTime.UnixEpoch : entry.ActiveFrom;
        var activeFromUtc = EnsureUtc(activeFrom);
        var registrationNorm = string.IsNullOrWhiteSpace(entry.RegistrationNorm)
            ? NormalizeBillingComparable(registration)
            : NormalizeBillingComparable(entry.RegistrationNorm);
        var imeiNorm = string.IsNullOrWhiteSpace(entry.ImeiNorm)
            ? NormalizeBillingDigits(entry.Imei)
            : NormalizeBillingDigits(entry.ImeiNorm);
        var iccidNorm = string.IsNullOrWhiteSpace(entry.IccidNorm)
            ? NormalizeBillingDigits(entry.Iccid)
            : NormalizeBillingDigits(entry.IccidNorm);
        var serialNorm = string.IsNullOrWhiteSpace(entry.SerialNumberNorm)
            ? NormalizeBillingComparable(entry.SerialNumber)
            : NormalizeBillingComparable(entry.SerialNumberNorm);

        return new BillingSyncRow
        {
            DocId = BuildBillingDocId(company, registration, imeiNorm, iccidNorm, serialNorm, activeFromUtc),
            Id = entry.Id,
            Company = company,
            Registration = registration,
            FleetNumber = NormalizeDisplayText(entry.FleetNumber),
            Make = NormalizeDisplayText(entry.Make),
            Model = NormalizeDisplayText(entry.Model),
            Colour = NormalizeDisplayText(entry.Colour),
            VinNumber = NormalizeDisplayText(entry.VinNumber),
            TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(entry.TrackingUnitMake) ?? NormalizeDisplayText(entry.TrackingUnitMake),
            StingPackageType = StingPackageCatalog.Normalize(entry.StingPackageType)
                               ?? StingPackageCatalog.Normalize(entry.TrackingUnitMake)
                               ?? NormalizeDisplayText(entry.StingPackageType),
            Notes = NormalizeDisplayText(entry.Notes),
            Reason = NormalizeDisplayText(entry.Reason),
            Imei = NormalizeDisplayText(entry.Imei),
            SerialNumber = NormalizeDisplayText(entry.SerialNumber),
            Iccid = NormalizeDisplayText(entry.Iccid),
            SimNumber = NormalizeDisplayText(entry.SimNumber),
            Status = entry.Status,
            ActiveFrom = activeFromUtc,
            ActiveTo = entry.ActiveTo is DateTime activeTo ? EnsureUtc(activeTo) : null,
            ArchivedAt = entry.ArchivedAt is DateTime archivedAt ? EnsureUtc(archivedAt) : null,
            RegistrationNorm = registrationNorm,
            ImeiNorm = imeiNorm,
            IccidNorm = iccidNorm,
            SerialNumberNorm = serialNorm
        };
    }

    private static BillingSyncRow? MapBillingDocumentToSyncRow(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();
        var company = NormalizeDisplayText(ReadString(data, "company")) ?? string.Empty;
        var registration = NormalizeBillingComparable(ReadString(data, "registration"));
        var activeFrom = GetDateTime(data, "activeFromUtc") ?? DateTime.UnixEpoch;
        var activeFromUtc = EnsureUtc(activeFrom);
        var registrationNorm = NormalizeBillingComparable(ReadString(data, "registrationNorm"));
        if (string.IsNullOrWhiteSpace(registrationNorm))
            registrationNorm = NormalizeBillingComparable(registration);

        var imeiNorm = NormalizeBillingDigits(ReadString(data, "imeiNorm"));
        var iccidNorm = NormalizeBillingDigits(ReadString(data, "iccidNorm"));
        var serialNorm = NormalizeBillingComparable(ReadString(data, "serialNumberNorm"));

        if (string.IsNullOrWhiteSpace(imeiNorm))
            imeiNorm = NormalizeBillingDigits(ReadString(data, "imei"));
        if (string.IsNullOrWhiteSpace(iccidNorm))
            iccidNorm = NormalizeBillingDigits(ReadString(data, "iccid"));
        if (string.IsNullOrWhiteSpace(serialNorm))
            serialNorm = NormalizeBillingComparable(ReadString(data, "serialNumber"));

        if (string.IsNullOrWhiteSpace(registration)
            && string.IsNullOrWhiteSpace(imeiNorm)
            && string.IsNullOrWhiteSpace(iccidNorm)
            && string.IsNullOrWhiteSpace(serialNorm))
        {
            return null;
        }

        var status = ParseBillingStatus(ReadString(data, "status"), ReadInt(data, "statusCode") ?? 0);

        return new BillingSyncRow
        {
            DocId = string.IsNullOrWhiteSpace(doc.Id)
                ? BuildBillingDocId(company, registration, imeiNorm, iccidNorm, serialNorm, activeFromUtc)
                : doc.Id,
            Id = ReadInt(data, "sourceLocalId") ?? 0,
            Company = company,
            Registration = registration,
            FleetNumber = NormalizeDisplayText(ReadString(data, "fleetNumber")),
            Make = NormalizeDisplayText(ReadString(data, "make")),
            Model = NormalizeDisplayText(ReadString(data, "model")),
            Colour = NormalizeDisplayText(ReadString(data, "colour")),
            VinNumber = NormalizeDisplayText(ReadString(data, "vinNumber")),
            TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(ReadString(data, "trackingUnitMake"))
                               ?? NormalizeDisplayText(ReadString(data, "trackingUnitMake")),
            StingPackageType = StingPackageCatalog.Normalize(ReadString(data, "stingPackageType"))
                               ?? StingPackageCatalog.Normalize(ReadString(data, "trackingUnitMake")),
            Notes = NormalizeDisplayText(ReadString(data, "notes")),
            Reason = NormalizeDisplayText(ReadString(data, "reason")),
            Imei = NormalizeDisplayText(ReadString(data, "imei")),
            SerialNumber = NormalizeDisplayText(ReadString(data, "serialNumber")),
            Iccid = NormalizeDisplayText(ReadString(data, "iccid")),
            SimNumber = NormalizeDisplayText(ReadString(data, "simNumber")),
            Status = status,
            ActiveFrom = activeFromUtc,
            ActiveTo = GetDateTime(data, "activeToUtc"),
            ArchivedAt = GetDateTime(data, "archivedAtUtc"),
            RegistrationNorm = registrationNorm,
            ImeiNorm = imeiNorm,
            IccidNorm = iccidNorm,
            SerialNumberNorm = serialNorm,
            RemoteUpdatedAtUtc = GetDocumentUpdatedAtUtc(doc, data)
        };
    }

    private static BillingEntry? FindLocalBillingEntry(
        IReadOnlyCollection<BillingEntry> localRows,
        BillingSyncRow remoteRow)
    {
        var byDocId = localRows.FirstOrDefault(x =>
            string.Equals(BuildBillingDocId(x), remoteRow.DocId, StringComparison.Ordinal));
        if (byDocId is not null)
            return byDocId;

        if (!string.IsNullOrWhiteSpace(remoteRow.ImeiNorm))
        {
            var byImei = localRows.FirstOrDefault(x =>
                string.Equals(
                    NormalizeBillingDigits(string.IsNullOrWhiteSpace(x.ImeiNorm) ? x.Imei : x.ImeiNorm),
                    remoteRow.ImeiNorm,
                    StringComparison.Ordinal));
            if (byImei is not null)
                return byImei;
        }

        if (!string.IsNullOrWhiteSpace(remoteRow.IccidNorm))
        {
            var byIccid = localRows.FirstOrDefault(x =>
                string.Equals(
                    NormalizeBillingDigits(string.IsNullOrWhiteSpace(x.IccidNorm) ? x.Iccid : x.IccidNorm),
                    remoteRow.IccidNorm,
                    StringComparison.Ordinal));
            if (byIccid is not null)
                return byIccid;
        }

        if (!string.IsNullOrWhiteSpace(remoteRow.SerialNumberNorm))
        {
            var bySerial = localRows.FirstOrDefault(x =>
                string.Equals(
                    NormalizeBillingComparable(string.IsNullOrWhiteSpace(x.SerialNumberNorm) ? x.SerialNumber : x.SerialNumberNorm),
                    remoteRow.SerialNumberNorm,
                    StringComparison.Ordinal));
            if (bySerial is not null)
                return bySerial;
        }

        var companyNorm = NormalizeBillingComparable(remoteRow.Company);
        var registrationNorm = NormalizeBillingComparable(remoteRow.Registration);
        if (string.IsNullOrWhiteSpace(companyNorm) || string.IsNullOrWhiteSpace(registrationNorm))
            return null;

        var remoteActiveFromUtc = EnsureUtc(remoteRow.ActiveFrom);
        return localRows.FirstOrDefault(x =>
        {
            var localCompany = NormalizeBillingComparable(x.Company);
            var localRegistration = NormalizeBillingComparable(x.Registration);
            if (!string.Equals(localCompany, companyNorm, StringComparison.Ordinal)
                || !string.Equals(localRegistration, registrationNorm, StringComparison.Ordinal))
            {
                return false;
            }

            var localActiveFrom = x.ActiveFrom == default ? DateTime.UnixEpoch : EnsureUtc(x.ActiveFrom);
            return Math.Abs((localActiveFrom - remoteActiveFromUtc).TotalSeconds) <= 1;
        });
    }

    private static void ApplyBillingSyncRow(BillingEntry entry, BillingSyncRow row)
    {
        entry.Company = row.Company;
        entry.Registration = row.Registration;
        entry.FleetNumber = row.FleetNumber;
        entry.Make = row.Make;
        entry.Model = row.Model;
        entry.Colour = row.Colour;
        entry.VinNumber = row.VinNumber;
        entry.TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(row.TrackingUnitMake) ?? row.TrackingUnitMake;
        entry.StingPackageType = StingPackageCatalog.Normalize(row.StingPackageType)
                                 ?? StingPackageCatalog.Normalize(row.TrackingUnitMake)
                                 ?? row.StingPackageType;
        entry.Notes = row.Notes;
        entry.Reason = row.Reason;
        entry.Imei = row.Imei;
        entry.SerialNumber = row.SerialNumber;
        entry.Iccid = row.Iccid;
        entry.SimNumber = row.SimNumber;
        entry.Status = row.Status;
        entry.ActiveFrom = EnsureUtc(row.ActiveFrom);
        entry.ActiveTo = row.ActiveTo is DateTime activeTo ? EnsureUtc(activeTo) : null;
        entry.ArchivedAt = row.ArchivedAt is DateTime archivedAt ? EnsureUtc(archivedAt) : null;
        entry.RegistrationNorm = string.IsNullOrWhiteSpace(row.RegistrationNorm)
            ? NormalizeBillingComparable(row.Registration)
            : NormalizeBillingComparable(row.RegistrationNorm);
        entry.ImeiNorm = string.IsNullOrWhiteSpace(row.ImeiNorm)
            ? NormalizeBillingDigits(row.Imei)
            : NormalizeBillingDigits(row.ImeiNorm);
        entry.IccidNorm = string.IsNullOrWhiteSpace(row.IccidNorm)
            ? NormalizeBillingDigits(row.Iccid)
            : NormalizeBillingDigits(row.IccidNorm);
        entry.SerialNumberNorm = string.IsNullOrWhiteSpace(row.SerialNumberNorm)
            ? NormalizeBillingComparable(row.SerialNumber)
            : NormalizeBillingComparable(row.SerialNumberNorm);
    }

    private static Dictionary<string, object?> BuildBillingPayload(BillingSyncRow row, Timestamp now)
    {
        return new Dictionary<string, object?>
        {
            ["sourceLocalId"] = row.Id,
            ["company"] = row.Company,
            ["registration"] = row.Registration,
            ["fleetNumber"] = row.FleetNumber,
            ["make"] = row.Make,
            ["model"] = row.Model,
            ["colour"] = row.Colour,
            ["vinNumber"] = row.VinNumber,
            ["trackingUnitMake"] = row.TrackingUnitMake,
            ["stingPackageType"] = row.StingPackageType,
            ["notes"] = row.Notes,
            ["reason"] = row.Reason,
            ["imei"] = row.Imei,
            ["serialNumber"] = row.SerialNumber,
            ["iccid"] = row.Iccid,
            ["simNumber"] = row.SimNumber,
            ["status"] = row.Status.ToString(),
            ["statusCode"] = (int)row.Status,
            ["activeFromUtc"] = Timestamp.FromDateTime(EnsureUtc(row.ActiveFrom)),
            ["activeToUtc"] = row.ActiveTo is DateTime activeTo
                ? Timestamp.FromDateTime(EnsureUtc(activeTo))
                : null,
            ["archivedAtUtc"] = row.ArchivedAt is DateTime archivedAt
                ? Timestamp.FromDateTime(EnsureUtc(archivedAt))
                : null,
            ["registrationNorm"] = row.RegistrationNorm,
            ["imeiNorm"] = row.ImeiNorm,
            ["iccidNorm"] = row.IccidNorm,
            ["serialNumberNorm"] = row.SerialNumberNorm,
            ["desktopSyncedAtUtc"] = now
        };
    }

    private static string BuildBillingDocId(BillingEntry entry)
    {
        var activeFrom = entry.ActiveFrom == default ? DateTime.UnixEpoch : entry.ActiveFrom;
        var registrationNorm = string.IsNullOrWhiteSpace(entry.RegistrationNorm)
            ? NormalizeBillingComparable(entry.Registration)
            : NormalizeBillingComparable(entry.RegistrationNorm);
        var imeiNorm = string.IsNullOrWhiteSpace(entry.ImeiNorm)
            ? NormalizeBillingDigits(entry.Imei)
            : NormalizeBillingDigits(entry.ImeiNorm);
        var iccidNorm = string.IsNullOrWhiteSpace(entry.IccidNorm)
            ? NormalizeBillingDigits(entry.Iccid)
            : NormalizeBillingDigits(entry.IccidNorm);
        var serialNorm = string.IsNullOrWhiteSpace(entry.SerialNumberNorm)
            ? NormalizeBillingComparable(entry.SerialNumber)
            : NormalizeBillingComparable(entry.SerialNumberNorm);

        return BuildBillingDocId(
            entry.Company,
            registrationNorm,
            imeiNorm,
            iccidNorm,
            serialNorm,
            activeFrom);
    }

    private static string BuildBillingDocId(
        string? company,
        string? registration,
        string? imeiNorm,
        string? iccidNorm,
        string? serialNorm,
        DateTime activeFrom)
    {
        var activeTicks = EnsureUtc(activeFrom).Ticks;
        if (!string.IsNullOrWhiteSpace(imeiNorm))
            return $"imei-{imeiNorm}-{activeTicks}";
        if (!string.IsNullOrWhiteSpace(iccidNorm))
            return $"iccid-{iccidNorm}-{activeTicks}";
        if (!string.IsNullOrWhiteSpace(serialNorm))
            return $"serial-{serialNorm}-{activeTicks}";

        var companyNorm = NormalizeBillingComparable(company);
        var registrationNorm = NormalizeBillingComparable(registration);
        if (!string.IsNullOrWhiteSpace(companyNorm) && !string.IsNullOrWhiteSpace(registrationNorm))
            return $"reg-{companyNorm}-{registrationNorm}-{activeTicks}";

        return $"row-{activeTicks}";
    }

    private static List<JobCardListItem> ApplyJobCardFilters(List<JobCardListItem> rows, JobCardQuery query)
    {
        IEnumerable<JobCardListItem> filtered = rows;

        if (!string.Equals(query.SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobStatus>(query.SelectedStatus, true, out var status))
        {
            filtered = filtered.Where(r => string.Equals(r.Status, status.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(query.SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobType>(query.SelectedType, true, out var type))
        {
            filtered = filtered.Where(r => string.Equals(r.Type, type.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.CompanyFilter))
        {
            var s = query.CompanyFilter.Trim();
            filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.Company)
                                           && r.Company.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.RegistrationFilter))
        {
            var s = query.RegistrationFilter.Trim();
            filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.Registration)
                                           && r.Registration.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (query.StartDate != null)
        {
            var start = query.StartDate.Value.Date;
            filtered = filtered.Where(r => r.CreatedAt >= start);
        }

        if (query.EndDate != null)
        {
            var endExclusive = query.EndDate.Value.Date.AddDays(1);
            filtered = filtered.Where(r => r.CreatedAt < endExclusive);
        }

        if (query.QuoteIdFilter.HasValue)
        {
            var quoteId = query.QuoteIdFilter.Value;
            filtered = filtered.Where(r => r.QuoteId == quoteId);
        }

        var materialized = query.QuoteIdFilter.HasValue
            ? filtered.OrderBy(r => r.JobCardNumber).ThenBy(r => r.CreatedAt).ToList()
            : filtered.OrderByDescending(r => r.CreatedAt).ToList();

        return materialized;
    }

    private static QuotePageResult ApplyQuoteFiltersAndPaging(List<QuoteListItem> rows, QuoteQuery query)
    {
        IEnumerable<QuoteListItem> filtered = rows;

        if (!string.Equals(query.SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteStatus>(query.SelectedStatus, true, out var status))
        {
            filtered = filtered.Where(r => string.Equals(r.Status, status.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(query.SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteType>(query.SelectedType, true, out var type))
        {
            filtered = filtered.Where(r => string.Equals(r.Type, type.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.CompanyFilter))
        {
            var s = query.CompanyFilter.Trim();
            filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.Company)
                                           && r.Company.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.RegistrationFilter))
        {
            var s = query.RegistrationFilter.Trim();
            filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.Registration)
                                           && r.Registration.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (query.StartDate != null)
        {
            var start = query.StartDate.Value.Date;
            filtered = filtered.Where(r => r.CreatedAt >= start);
        }

        if (query.EndDate != null)
        {
            var endExclusive = query.EndDate.Value.Date.AddDays(1);
            filtered = filtered.Where(r => r.CreatedAt < endExclusive);
        }

        var ordered = filtered.OrderByDescending(r => r.CreatedAt).ToList();
        var totalCount = ordered.Count;

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 100 : query.PageSize;
        var skip = (pageNumber - 1) * pageSize;
        var paged = ordered.Skip(skip).Take(pageSize).ToList();

        return new QuotePageResult
        {
            TotalCount = totalCount,
            Items = paged
        };
    }

    private static Client? MapClientDocument(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();
        if (!data.TryGetValue("name", out var nameObj))
            return null;

        var created = ReadTimestamp(data, "createdAtUtc") ?? DateTime.UtcNow;

        return new Client
        {
            Id = ReadInt(data, "id") ?? ParseIntOrDefault(doc.Id),
            Name = ReadString(data, "name") ?? string.Empty,
            NameNorm = ReadString(data, "nameNorm") ?? NormalizeComparableText(ReadString(data, "name")),
            ContactPerson = ReadString(data, "contactPerson"),
            PhoneNumber = ReadString(data, "phoneNumber"),
            EmailAddress = ReadString(data, "emailAddress"),
            Address = ReadString(data, "address"),
            CreatedAt = created
        };
    }

    private static JobCardListItem? MapJobCardDocument(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();
        if (!data.TryGetValue("id", out _))
            return null;

        var typeText = ReadString(data, "type") ?? JobType.Install.ToString();
        var statusText = ReadString(data, "status") ?? JobStatus.Open.ToString();

        if (!Enum.TryParse<JobType>(typeText, true, out var type))
            type = JobType.Install;

        if (!Enum.TryParse<JobStatus>(statusText, true, out _))
            statusText = JobStatus.Open.ToString();

        var created = ReadTimestamp(data, "createdAtUtc") ?? DateTime.UtcNow;
        var lastPhotoAt = ReadTimestamp(data, "lastPhotoAtUtc");
        var scheduledFor = ReadTimestamp(data, "scheduledForUtc");

        return new JobCardListItem
        {
            Id = ReadInt(data, "id") ?? ParseIntOrDefault(doc.Id),
            JobCardNumber = ReadInt(data, "jobCardNumber") ?? 0,
            JobCardReference = ReadString(data, "jobCardReference") ?? string.Empty,
            QuoteId = ReadInt(data, "quoteId"),
            QuoteReference = ReadString(data, "quoteReference") ?? "-",
            JobTypeValue = type,
            Type = typeText,
            Status = statusText,
            PhotoCount = ReadInt(data, "photoCount") ?? 0,
            LastPhotoAt = lastPhotoAt,
            Company = ReadString(data, "company") ?? string.Empty,
            Registration = ReadString(data, "registration") ?? string.Empty,
            Make = ReadString(data, "make"),
            Model = ReadString(data, "model"),
            Imei = ReadString(data, "imei"),
            SerialNumber = ReadString(data, "serialNumber"),
            Iccid = ReadString(data, "iccid"),
            ScheduledFor = scheduledFor,
            CreatedAt = created
        };
    }

    private static QuoteListItem? MapQuoteDocument(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();
        if (!data.TryGetValue("id", out _))
            return null;

        return new QuoteListItem
        {
            Id = ReadInt(data, "id") ?? ParseIntOrDefault(doc.Id),
            QuoteNumber = ReadInt(data, "quoteNumber") ?? 0,
            Type = ReadString(data, "type") ?? QuoteType.Install.ToString(),
            Status = ReadString(data, "status") ?? QuoteStatus.Draft.ToString(),
            Company = ReadString(data, "company") ?? string.Empty,
            Registration = ReadString(data, "registration"),
            ProductType = ReadString(data, "productType"),
            AmountExVat = ReadDecimal(data, "amountExVat"),
            CreatedAt = ReadTimestamp(data, "createdAtUtc") ?? DateTime.UtcNow
        };
    }

    private static int ParseIntOrDefault(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
            return null;

        return value.ToString();
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
            return 0m;

        return value switch
        {
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, out var parsed) => parsed,
            _ => 0m
        };
    }

    private static DateTime? ReadTimestamp(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            Timestamp ts => ts.ToDateTime(),
            DateTime dt => dt,
            _ => null
        };
    }

    private static DateTime EnsureUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static DateTime? GetDateTime(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            Timestamp timestamp => timestamp.ToDateTime(),
            DateTime dateTime => EnsureUtc(dateTime),
            string text when DateTime.TryParse(text, out var parsed) => EnsureUtc(parsed),
            _ => null
        };
    }

    private static DateTime GetDocumentUpdatedAtUtc(DocumentSnapshot doc, IReadOnlyDictionary<string, object> data)
    {
        if (doc.UpdateTime is Timestamp updateTime)
            return EnsureUtc(updateTime.ToDateTime());

        return GetDateTime(data, "desktopSyncedAtUtc") ?? DateTime.UnixEpoch;
    }

    private static string? NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeBillingComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeBillingDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static BillingStatus ParseBillingStatus(string? statusText, int statusCode)
    {
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            if (Enum.TryParse<BillingStatus>(statusText.Trim(), true, out var parsed))
                return parsed;

            var normalized = statusText.Trim().ToUpperInvariant();
            if (normalized.Contains("REMOV", StringComparison.Ordinal))
                return BillingStatus.Removed;

            if (normalized.Contains("NOT LOADED", StringComparison.Ordinal)
                || normalized.Contains("NOTLOADED", StringComparison.Ordinal)
                || normalized.Contains("INACTIVE", StringComparison.Ordinal))
            {
                return BillingStatus.NotLoaded;
            }
        }

        if (Enum.IsDefined(typeof(BillingStatus), statusCode))
            return (BillingStatus)statusCode;

        return BillingStatus.Active;
    }

    private async Task<bool> EnsureFirestoreReadyAsync(CancellationToken cancellationToken)
    {
        if (_firestore is not null)
            return true;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_firestore is not null)
                return true;

            if (_initAttempted && _firestore is null)
                return false;

            _initAttempted = true;

            if (string.IsNullOrWhiteSpace(_settings.FirebaseProjectId)
                || string.IsNullOrWhiteSpace(_settings.FirebaseServiceAccountJsonPath))
            {
                _initError = "Firestore primary mode requires Firebase Project ID and Service Account JSON path.";
                return false;
            }

            if (!File.Exists(_settings.FirebaseServiceAccountJsonPath))
            {
                _initError = $"Service account file not found: {_settings.FirebaseServiceAccountJsonPath}";
                return false;
            }

            var credential = GoogleCredential.FromFile(_settings.FirebaseServiceAccountJsonPath)
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

            _firestore = new FirestoreDbBuilder
            {
                ProjectId = _settings.FirebaseProjectId,
                Credential = credential
            }.Build();

            _initError = null;
            return true;
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            _firestore = null;
            return false;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
