using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class FirestoreDataStore : IDataStore
{
    private readonly AppSettings _settings;
    private readonly LocalSqliteDataStore _local = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private long _jobCardMirrorInFlight;
    private DateTime _lastJobCardMirrorUtc = DateTime.MinValue;
    private static readonly TimeSpan JobCardMirrorInterval = TimeSpan.FromSeconds(45);

    private FirestoreDb? _firestore;
    private bool _initAttempted;
    private string? _initError;

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
