using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class FirebaseSyncService : IDisposable
{
    private static readonly Lazy<FirebaseSyncService> LazyInstance = new(() => new FirebaseSyncService());
    private static readonly string[] RequiredVerificationMarkers =
    {
        "[Verification:Vehicle]",
        "[Verification:Registration]",
        "[Verification:VIN]",
        "[Verification:TrackingUnit]",
        "[Verification:SerialIccid]"
    };

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _syncTrigger = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private bool _disposed;
    private Action<string, bool>? _statusSink;
    private AppSettings? _settings;
    private FirestoreDb? _firestore;
    private StorageClient? _storage;
    private FirestoreChangeListener? _pendingSubmissionListener;
    private readonly HashSet<int> _knownOpenJobCardIds = new();
    private readonly Dictionary<int, string> _openPayloadHashes = new();
    private readonly Dictionary<int, string> _completedPayloadHashes = new();
    private readonly Dictionary<string, string> _mobileUserPayloadHashes = new(StringComparer.Ordinal);
    private bool _openSnapshotInitialized;
    private bool _openRemoteCacheInitialized;
    private bool _completedRemoteCacheInitialized;
    private bool _mobileUsersRemoteCacheInitialized;
    private int _pendingTriggerFlag;
    private bool _localJobCardChangeSubscribed;

    public static FirebaseSyncService Instance => LazyInstance.Value;
    public bool IsRunning => _workerTask is { IsCompleted: false };

    private sealed class JobCardSyncRow
    {
        public int Id { get; init; }
        public int? QuoteId { get; init; }
        public int JobCardNumber { get; init; }
        public JobType Type { get; init; }
        public JobStatus Status { get; init; }
        public string Company { get; init; } = string.Empty;
        public string Registration { get; init; } = string.Empty;
        public string? FleetNumber { get; init; }
        public string? Make { get; init; }
        public string? Model { get; init; }
        public string? Colour { get; init; }
        public string? VinNumber { get; init; }
        public string? GridLocation { get; init; }
        public string? TrackingUnitMake { get; init; }
        public string? Imei { get; init; }
        public string? SerialNumber { get; init; }
        public string? Iccid { get; init; }
        public string? SimNumber { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ScheduledFor { get; init; }
        public DateTime? CompletedAt { get; init; }
    }

    private sealed class MobileUserSyncRow
    {
        public string Username { get; init; } = string.Empty;
        public string UsernameNorm { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public string PasswordHash { get; init; } = string.Empty;
        public string PasswordSalt { get; init; } = string.Empty;
    }

    private FirebaseSyncService()
    {
    }

    public async Task<(bool started, string message)> StartAsync(AppSettings settings, Action<string, bool>? statusSink = null)
    {
        await _syncLock.WaitAsync();
        try
        {
            if (_disposed)
                return (false, "Firebase sync service is disposed.");

            _statusSink = statusSink;
            _settings = settings;
            if (IsRunning)
                return (true, "Firebase sync is already running.");

            UnsubscribeFromLocalJobCardChanges();
            _knownOpenJobCardIds.Clear();
            _openPayloadHashes.Clear();
            _completedPayloadHashes.Clear();
            _mobileUserPayloadHashes.Clear();
            _openSnapshotInitialized = false;
            _openRemoteCacheInitialized = false;
            _completedRemoteCacheInitialized = false;
            _mobileUsersRemoteCacheInitialized = false;
            _pendingTriggerFlag = 0;
            _localJobCardChangeSubscribed = false;
            while (_syncTrigger.CurrentCount > 0)
                _ = _syncTrigger.Wait(0);

            if (!settings.FirebaseSyncEnabled)
                return (false, "Firebase sync is disabled.");

            var validationError = ValidateSettings(settings);
            if (!string.IsNullOrWhiteSpace(validationError))
                return (false, validationError);

            _cts = new CancellationTokenSource();
            _workerTask = RunLoopAsync(_cts.Token);
            return (true, "Firebase sync started.");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            if (_cts is null)
                return;

            _cts.Cancel();
            UnsubscribeFromLocalJobCardChanges();
            await StopPendingSubmissionListenerAsync();
            if (_workerTask is not null)
            {
                try
                {
                    await _workerTask;
                }
                catch (TaskCanceledException)
                {
                }
            }

            _cts.Dispose();
            _cts = null;
            _workerTask = null;
            _firestore = null;
            _storage = null;
            _knownOpenJobCardIds.Clear();
            _openPayloadHashes.Clear();
            _completedPayloadHashes.Clear();
            _mobileUserPayloadHashes.Clear();
            _openSnapshotInitialized = false;
            _openRemoteCacheInitialized = false;
            _completedRemoteCacheInitialized = false;
            _mobileUsersRemoteCacheInitialized = false;
            _pendingTriggerFlag = 0;
            _localJobCardChangeSubscribed = false;
            while (_syncTrigger.CurrentCount > 0)
                _ = _syncTrigger.Wait(0);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = StopAsync();
        _syncLock.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureClientsAsync(cancellationToken);
            EnsurePendingSubmissionListener();
            SubscribeToLocalJobCardChanges();
        }
        catch (Exception ex)
        {
            if (!TryHandleFatalSyncException(ex))
                _statusSink?.Invoke($"Firebase sync startup failed: {ex.Message}", true);
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (TryHandleFatalSyncException(ex))
                        break;

                    _statusSink?.Invoke($"Firebase sync failed: {ex.Message}", true);
                }

                var waitSeconds = Math.Clamp(_settings?.FirebaseSyncIntervalSeconds ?? 5, 2, 3600);
                try
                {
                    var immediate = await _syncTrigger.WaitAsync(TimeSpan.Zero, cancellationToken);
                    if (!immediate)
                    {
                        immediate = await _syncTrigger.WaitAsync(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                    }

                    if (immediate)
                        Interlocked.Exchange(ref _pendingTriggerFlag, 0);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            UnsubscribeFromLocalJobCardChanges();
            await StopPendingSubmissionListenerAsync();
        }
    }

    private bool TryHandleFatalSyncException(Exception ex)
    {
        if (_settings is null)
            return false;

        var message = ex.ToString();
        if (IsFirestoreApiDisabled(message))
        {
            _settings.FirebaseSyncEnabled = false;
            if (_settings.FirestorePrimaryDataEnabled)
                _settings.FirestorePrimaryDataEnabled = false;

            new SettingsService().Save(_settings);
            _statusSink?.Invoke(
                "Firebase sync disabled: Cloud Firestore API is not enabled for the configured project. " +
                "Enable Firestore API in Google Cloud Console, then re-enable Firebase Sync in Settings.",
                false);
            return true;
        }

        return false;
    }

    private static bool IsFirestoreApiDisabled(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)
            && message.Contains("firestore.googleapis.com", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase)
                || message.Contains("is disabled", StringComparison.OrdinalIgnoreCase));
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        await EnsureClientsAsync(cancellationToken);
        EnsurePendingSubmissionListener();
        var changedMobileUsers = await PublishMobileUsersAsync(cancellationToken);
        var (exportedOpenCount, changedOpenCount) = await PublishOpenJobCardsAsync(cancellationToken);
        var (exportedCompletedCount, changedCompletedCount) = await PublishCompletedJobCardsAsync(cancellationToken);
        var importedCount = await ImportPhotoSubmissionsAsync(cancellationToken);

        if (importedCount > 0)
            _statusSink?.Invoke($"Firebase sync: imported {importedCount} technician photo submission(s).", false);
        else if (changedOpenCount > 0 || changedCompletedCount > 0 || changedMobileUsers > 0)
            _statusSink?.Invoke(
                $"Firebase sync: applied {changedOpenCount + changedCompletedCount + changedMobileUsers} update(s) " +
                $"({exportedOpenCount} open, {exportedCompletedCount} completed, {changedMobileUsers} mobile user records).",
                false);
    }

    private async Task EnsureClientsAsync(CancellationToken cancellationToken)
    {
        if (_settings is null)
            throw new InvalidOperationException("Firebase settings are not loaded.");

        if (_firestore is not null && _storage is not null)
            return;

        var validationError = ValidateSettings(_settings);
        if (!string.IsNullOrWhiteSpace(validationError))
            throw new InvalidOperationException(validationError);

        var credential = GoogleCredential.FromFile(_settings.FirebaseServiceAccountJsonPath!)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");

        _firestore = new FirestoreDbBuilder
        {
            ProjectId = _settings.FirebaseProjectId!,
            Credential = credential
        }.Build();

        _storage = await StorageClient.CreateAsync(credential);
    }

    private void EnsurePendingSubmissionListener()
    {
        if (_firestore is null || _pendingSubmissionListener is not null)
            return;

        var query = _firestore.Collection(FirestoreCollections.PhotoSubmissions)
            .WhereEqualTo("importStatus", "pending")
            .Limit(1);

        _pendingSubmissionListener = query.Listen(snapshot =>
        {
            if (snapshot is { Count: > 0 })
                QueueImmediateSync();
        });

        _statusSink?.Invoke("Firebase sync realtime listener active.", false);
    }

    private async Task StopPendingSubmissionListenerAsync()
    {
        var listener = _pendingSubmissionListener;
        _pendingSubmissionListener = null;
        if (listener is null)
            return;

        try
        {
            await listener.StopAsync();
        }
        catch
        {
            // ignore listener shutdown failures
        }
    }

    private void SubscribeToLocalJobCardChanges()
    {
        if (_localJobCardChangeSubscribed)
            return;

        LocalDataChangeNotifier.JobCardsChanged -= OnLocalJobCardsChanged;
        LocalDataChangeNotifier.JobCardsChanged += OnLocalJobCardsChanged;
        _localJobCardChangeSubscribed = true;
    }

    private void UnsubscribeFromLocalJobCardChanges()
    {
        LocalDataChangeNotifier.JobCardsChanged -= OnLocalJobCardsChanged;
        _localJobCardChangeSubscribed = false;
    }

    private void OnLocalJobCardsChanged()
    {
        QueueImmediateSync();
    }

    private void QueueImmediateSync()
    {
        if (Interlocked.Exchange(ref _pendingTriggerFlag, 1) == 1)
            return;

        try
        {
            _syncTrigger.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task<(int totalCount, int changedCount)> PublishOpenJobCardsAsync(CancellationToken cancellationToken)
    {
        if (_firestore is null)
            return (0, 0);

        using var db = new AppDbContext();
        var rows = db.JobCards
            .AsNoTracking()
            .Where(j => j.Status == JobStatus.Open)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobCardSyncRow
            {
                Id = j.Id,
                QuoteId = j.QuoteId,
                JobCardNumber = j.JobCardNumber,
                Type = j.Type,
                Status = j.Status,
                Company = j.Company,
                Registration = j.Registration,
                FleetNumber = j.FleetNumber,
                Make = j.Make,
                Model = j.Model,
                Colour = j.Colour,
                VinNumber = j.VinNumber,
                GridLocation = j.GridLocation,
                TrackingUnitMake = j.TrackingUnitMake,
                Imei = j.Imei,
                SerialNumber = j.SerialNumber,
                Iccid = j.Iccid,
                SimNumber = j.SimNumber,
                CompletedAt = j.CompletedAt,
                ScheduledFor = j.ScheduledFor,
                CreatedAt = j.CreatedAt
            })
            .ToList();

        var changedCount = await PublishJobCardsCollectionAsync(
            _firestore.Collection(FirestoreCollections.OpenJobCards),
            rows,
            _openPayloadHashes,
            isOpenCollection: true,
            cancellationToken);
        await NotifyTechniciansForNewJobCardsAsync(rows, cancellationToken);
        return (rows.Count, changedCount);
    }

    private async Task<(int totalCount, int changedCount)> PublishCompletedJobCardsAsync(CancellationToken cancellationToken)
    {
        if (_firestore is null)
            return (0, 0);

        using var db = new AppDbContext();
        var rows = db.JobCards
            .AsNoTracking()
            .Where(j => j.Status == JobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Select(j => new JobCardSyncRow
            {
                Id = j.Id,
                QuoteId = j.QuoteId,
                JobCardNumber = j.JobCardNumber,
                Type = j.Type,
                Status = j.Status,
                Company = j.Company,
                Registration = j.Registration,
                FleetNumber = j.FleetNumber,
                Make = j.Make,
                Model = j.Model,
                Colour = j.Colour,
                VinNumber = j.VinNumber,
                GridLocation = j.GridLocation,
                TrackingUnitMake = j.TrackingUnitMake,
                Imei = j.Imei,
                SerialNumber = j.SerialNumber,
                Iccid = j.Iccid,
                SimNumber = j.SimNumber,
                CompletedAt = j.CompletedAt,
                ScheduledFor = j.ScheduledFor,
                CreatedAt = j.CreatedAt
            })
            .ToList();

        var changedCount = await PublishJobCardsCollectionAsync(
            _firestore.Collection(FirestoreCollections.CompletedJobCards),
            rows,
            _completedPayloadHashes,
            isOpenCollection: false,
            cancellationToken);
        return (rows.Count, changedCount);
    }

    private async Task<int> PublishMobileUsersAsync(CancellationToken cancellationToken)
    {
        if (_firestore is null)
            return 0;

        using var db = new AppDbContext();
        var rows = db.UserAccounts
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new MobileUserSyncRow
            {
                Username = u.Username,
                UsernameNorm = u.UsernameNorm,
                Role = u.Role,
                IsActive = u.IsActive,
                PasswordHash = u.PasswordHash,
                PasswordSalt = u.PasswordSalt
            })
            .ToList()
            .Where(u => AuthService.CanAccessTechnicianApp(u.Role))
            .OrderBy(u => u.UsernameNorm, StringComparer.Ordinal)
            .ToList();

        var collection = _firestore.Collection(FirestoreCollections.MobileUsers);
        await EnsureMobileUsersRemoteCacheInitializedAsync(collection, cancellationToken);

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var expectedDocIds = new HashSet<string>(StringComparer.Ordinal);
        var changedCount = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.UsernameNorm))
                continue;

            var docId = row.UsernameNorm.Trim();
            expectedDocIds.Add(docId);

            var payloadFingerprint = BuildMobileUserPayloadFingerprint(row);
            if (_mobileUserPayloadHashes.TryGetValue(docId, out var knownFingerprint)
                && string.Equals(knownFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = new Dictionary<string, object?>
            {
                ["username"] = row.Username,
                ["usernameNorm"] = docId,
                ["role"] = row.Role,
                ["isActive"] = row.IsActive,
                ["passwordHash"] = row.PasswordHash,
                ["passwordSalt"] = row.PasswordSalt,
                ["updatedAtUtc"] = now
            };

            await collection.Document(docId).SetAsync(payload, SetOptions.MergeAll, cancellationToken);
            _mobileUserPayloadHashes[docId] = payloadFingerprint;
            changedCount++;
        }

        var staleIds = _mobileUserPayloadHashes.Keys
            .Where(id => !expectedDocIds.Contains(id))
            .ToList();

        foreach (var staleId in staleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await collection.Document(staleId).DeleteAsync(cancellationToken: cancellationToken);
            _mobileUserPayloadHashes.Remove(staleId);
            changedCount++;
        }

        return changedCount;
    }

    private async Task<int> PublishJobCardsCollectionAsync(
        CollectionReference collection,
        IReadOnlyCollection<JobCardSyncRow> rows,
        Dictionary<int, string> payloadHashes,
        bool isOpenCollection,
        CancellationToken cancellationToken)
    {
        await EnsureRemoteCacheInitializedAsync(collection, payloadHashes, isOpenCollection, cancellationToken);

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var expectedDocIds = new HashSet<int>();
        var changedCount = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            expectedDocIds.Add(row.Id);
            var payloadFingerprint = BuildJobCardPayloadFingerprint(row);
            if (payloadHashes.TryGetValue(row.Id, out var knownFingerprint)
                && string.Equals(knownFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = BuildJobCardPayload(row, now);
            var doc = collection.Document(row.Id.ToString());
            await doc.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
            payloadHashes[row.Id] = payloadFingerprint;
            changedCount++;
        }

        var staleIds = payloadHashes.Keys
            .Where(id => !expectedDocIds.Contains(id))
            .ToList();

        foreach (var staleId in staleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await collection.Document(staleId.ToString()).DeleteAsync(cancellationToken: cancellationToken);
            payloadHashes.Remove(staleId);
            changedCount++;
        }

        return changedCount;
    }

    private static Dictionary<string, object?> BuildJobCardPayload(JobCardSyncRow row, Timestamp now)
    {
        return new Dictionary<string, object?>
        {
            ["jobCardId"] = row.Id,
            ["jobCardReference"] = JobCardReferenceFormatter.Format(row.Type, row.JobCardNumber),
            ["quoteId"] = row.QuoteId,
            ["type"] = row.Type.ToString(),
            ["status"] = row.Status.ToString(),
            ["company"] = row.Company,
            ["registration"] = row.Registration,
            ["fleetNumber"] = row.FleetNumber,
            ["make"] = row.Make,
            ["model"] = row.Model,
            ["colour"] = row.Colour,
            ["vinNumber"] = row.VinNumber,
            ["gridLocation"] = row.GridLocation,
            ["trackingUnitMake"] = row.TrackingUnitMake,
            ["imei"] = row.Imei,
            ["serialNumber"] = row.SerialNumber,
            ["iccid"] = row.Iccid,
            ["simNumber"] = row.SimNumber,
            ["scheduledForUtc"] = row.ScheduledFor is DateTime scheduledFor
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(scheduledFor, DateTimeKind.Utc))
                : null,
            ["completedAtUtc"] = row.CompletedAt is DateTime completedAt
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(completedAt, DateTimeKind.Utc))
                : null,
            ["createdAtUtc"] = Timestamp.FromDateTime(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
            ["desktopSyncedAtUtc"] = now
        };
    }

    private static string BuildJobCardPayloadFingerprint(JobCardSyncRow row)
    {
        var builder = new StringBuilder();
        builder.Append(row.Id).Append('|')
            .Append(row.QuoteId?.ToString() ?? string.Empty).Append('|')
            .Append(row.JobCardNumber).Append('|')
            .Append(row.Type).Append('|')
            .Append(row.Status).Append('|')
            .Append(row.Company).Append('|')
            .Append(row.Registration).Append('|')
            .Append(row.FleetNumber ?? string.Empty).Append('|')
            .Append(row.Make ?? string.Empty).Append('|')
            .Append(row.Model ?? string.Empty).Append('|')
            .Append(row.Colour ?? string.Empty).Append('|')
            .Append(row.VinNumber ?? string.Empty).Append('|')
            .Append(row.GridLocation ?? string.Empty).Append('|')
            .Append(row.TrackingUnitMake ?? string.Empty).Append('|')
            .Append(row.Imei ?? string.Empty).Append('|')
            .Append(row.SerialNumber ?? string.Empty).Append('|')
            .Append(row.Iccid ?? string.Empty).Append('|')
            .Append(row.SimNumber ?? string.Empty).Append('|')
            .Append(row.CreatedAt.ToUniversalTime().Ticks).Append('|')
            .Append(row.ScheduledFor?.ToUniversalTime().Ticks.ToString() ?? string.Empty).Append('|')
            .Append(row.CompletedAt?.ToUniversalTime().Ticks.ToString() ?? string.Empty);
        return builder.ToString();
    }

    private static string BuildMobileUserPayloadFingerprint(MobileUserSyncRow row)
    {
        return string.Join("|",
            row.UsernameNorm ?? string.Empty,
            row.Username ?? string.Empty,
            row.Role ?? string.Empty,
            row.IsActive ? "1" : "0",
            row.PasswordHash ?? string.Empty,
            row.PasswordSalt ?? string.Empty);
    }

    private async Task EnsureRemoteCacheInitializedAsync(
        CollectionReference collection,
        Dictionary<int, string> payloadHashes,
        bool isOpenCollection,
        CancellationToken cancellationToken)
    {
        var isInitialized = isOpenCollection ? _openRemoteCacheInitialized : _completedRemoteCacheInitialized;
        if (isInitialized)
            return;

        var snapshot = await collection.GetSnapshotAsync(cancellationToken);
        foreach (var existing in snapshot.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(existing.Id, out var id))
                continue;

            if (!payloadHashes.ContainsKey(id))
                payloadHashes[id] = string.Empty;
        }

        if (isOpenCollection)
            _openRemoteCacheInitialized = true;
        else
            _completedRemoteCacheInitialized = true;
    }

    private async Task EnsureMobileUsersRemoteCacheInitializedAsync(
        CollectionReference collection,
        CancellationToken cancellationToken)
    {
        if (_mobileUsersRemoteCacheInitialized)
            return;

        var snapshot = await collection.GetSnapshotAsync(cancellationToken);
        foreach (var existing in snapshot.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_mobileUserPayloadHashes.ContainsKey(existing.Id))
                _mobileUserPayloadHashes[existing.Id] = string.Empty;
        }

        _mobileUsersRemoteCacheInitialized = true;
    }

    private async Task NotifyTechniciansForNewJobCardsAsync(
        IReadOnlyCollection<JobCardSyncRow> openRows,
        CancellationToken cancellationToken)
    {
        var currentOpenIds = openRows.Select(x => x.Id).ToHashSet();
        if (!_openSnapshotInitialized)
        {
            _knownOpenJobCardIds.Clear();
            _knownOpenJobCardIds.UnionWith(currentOpenIds);
            _openSnapshotInitialized = true;
            return;
        }

        var newRows = openRows
            .Where(x => !_knownOpenJobCardIds.Contains(x.Id))
            .OrderBy(x => x.CreatedAt)
            .ToList();

        _knownOpenJobCardIds.Clear();
        _knownOpenJobCardIds.UnionWith(currentOpenIds);

        if (newRows.Count == 0 || _settings is null)
            return;

        var pushService = new FirebasePushNotificationService(_settings);
        if (!pushService.IsConfigured())
            return;

        var sent = 0;
        var failed = 0;
        foreach (var row in newRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reference = JobCardReferenceFormatter.Format(row.Type, row.JobCardNumber);
            var body = string.IsNullOrWhiteSpace(row.Registration)
                ? (row.Company ?? string.Empty)
                : $"{row.Company} - Reg {row.Registration}";

            var payload = new Dictionary<string, string>
            {
                ["jobCardId"] = row.Id.ToString(),
                ["jobCardReference"] = reference,
                ["company"] = row.Company ?? string.Empty,
                ["registration"] = row.Registration ?? string.Empty,
                ["type"] = row.Type.ToString()
            };

            var pushResult = await pushService.SendTopicNotificationAsync(
                topic: "technician-jobs",
                title: $"New Job Card {reference}",
                body: body,
                data: payload,
                cancellationToken: cancellationToken);

            if (pushResult.ok)
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        if (sent > 0)
            _statusSink?.Invoke($"Technician mobile ping sent for {sent} new job card(s).", false);

        if (failed > 0)
            _statusSink?.Invoke($"Failed to send {failed} technician mobile notification(s).", true);
    }

    private async Task<int> ImportPhotoSubmissionsAsync(CancellationToken cancellationToken)
    {
        if (_firestore is null || _storage is null || _settings is null)
            return 0;

        var collection = _firestore.Collection(FirestoreCollections.PhotoSubmissions);
        var pendingQuery = collection
            .WhereEqualTo("importStatus", "pending")
            .Limit(30);

        var pendingSnapshot = await pendingQuery.GetSnapshotAsync(cancellationToken);
        var docsById = new Dictionary<string, DocumentSnapshot>(StringComparer.Ordinal);
        foreach (var doc in pendingSnapshot.Documents)
            docsById[doc.Id] = doc;

        if (docsById.Count < 30)
        {
            var retryFailedSnapshot = await collection
                .WhereEqualTo("importStatus", "failed")
                .Limit(30)
                .GetSnapshotAsync(cancellationToken);

            foreach (var doc in retryFailedSnapshot.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (docsById.ContainsKey(doc.Id))
                    continue;
                if (!IsRetryableFailedSubmission(doc))
                    continue;

                docsById[doc.Id] = doc;
                if (docsById.Count >= 30)
                    break;
            }
        }

        if (docsById.Count == 0)
            return 0;

        var imported = 0;
        foreach (var doc in docsById.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await ImportSingleSubmissionAsync(doc, cancellationToken);
                if (result)
                    imported++;
            }
            catch (Exception ex)
            {
                await MarkSubmissionFailedAsync(doc.Reference, ex.Message, cancellationToken);
            }
        }

        return imported;
    }

    private static bool IsRetryableFailedSubmission(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();
        var importMessage = GetString(data, "importMessage");
        if (string.IsNullOrWhiteSpace(importMessage))
            return false;

        return importMessage.Contains("could not be translated", StringComparison.OrdinalIgnoreCase)
            || importMessage.Contains("StringComparison.OrdinalIgnoreCase", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ImportSingleSubmissionAsync(DocumentSnapshot doc, CancellationToken cancellationToken)
    {
        var data = doc.ToDictionary();
        var marker = $"[firebaseSubmission:{doc.Id}]";

        var jobCardId = GetInt(data, "jobCardId");
        if (jobCardId <= 0)
            throw new InvalidOperationException("Submission missing valid jobCardId.");

        var storagePath = GetString(data, "storagePath");
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new InvalidOperationException("Submission missing storagePath.");

        var technicianName = GetString(data, "technicianName");
        var notes = GetString(data, "notes");
        var originalFileName = GetString(data, "fileName");
        var gridLocation = GetString(data, "gridLocation");
        var isFinalInBatch = GetBool(data, "isFinalInBatch");
        var verificationMarker = ExtractVerificationMarker(notes);
        var verificationLabel = DescribeVerificationMarker(verificationMarker);
        var technicianDisplay = string.IsNullOrWhiteSpace(technicianName) ? "Technician" : technicianName.Trim();

        using (var db = new AppDbContext())
        {
            var jobExists = db.JobCards.AsNoTracking().Any(j => j.Id == jobCardId);
            if (!jobExists)
                throw new InvalidOperationException($"Job card {jobCardId} not found.");

            var alreadyImported = db.Attachments.AsNoTracking().Any(a =>
                a.OwnerType == AttachmentOwnerType.JobCard
                && a.OwnerId == jobCardId
                && a.Kind == AttachmentKind.JobPhoto
                && a.Notes != null
                && a.Notes.Contains(marker));

            if (alreadyImported)
            {
                await MarkSubmissionImportedAsync(doc.Reference, null, "Already imported.", cancellationToken);
                return false;
            }

            var duplicateByVerification = false;
            if (!string.IsNullOrWhiteSpace(verificationMarker))
            {
                var existingNotes = db.Attachments
                    .AsNoTracking()
                    .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                                && a.OwnerId == jobCardId
                                && a.Kind == AttachmentKind.JobPhoto
                                && a.Notes != null)
                    .Select(a => a.Notes!)
                    .ToList();

                duplicateByVerification = existingNotes.Any(note =>
                    note.Contains(verificationMarker, StringComparison.OrdinalIgnoreCase));
            }

            if (duplicateByVerification)
            {
                var actor = string.IsNullOrWhiteSpace(technicianName) ? "FirebaseTech" : $"FirebaseTech:{technicianName.Trim()}";
                var completionMessage = await ApplySubmissionJobCardUpdatesAsync(
                    jobCardId,
                    actor,
                    gridLocation,
                    cancellationToken);

                var importMessage = string.IsNullOrWhiteSpace(completionMessage)
                    ? $"Duplicate {verificationLabel} photo skipped."
                    : $"Duplicate {verificationLabel} photo skipped. {completionMessage}";

                await MarkSubmissionImportedAsync(doc.Reference, null, importMessage, cancellationToken);
                NotifyTechnicianDesktopEvents(jobCardId, technicianDisplay, verificationLabel, true, isFinalInBatch);
                return false;
            }
        }

        var (bucket, objectName) = ParseStorageReference(storagePath, _settings!.FirebaseStorageBucket);
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = Path.GetExtension(objectName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await _storage!.DownloadObjectAsync(bucket, objectName, stream, cancellationToken: cancellationToken);
            }

            var actor = string.IsNullOrWhiteSpace(technicianName) ? "FirebaseTech" : $"FirebaseTech:{technicianName.Trim()}";
            var notesWithMarker = string.IsNullOrWhiteSpace(notes)
                ? marker
                : $"{notes.Trim()}{Environment.NewLine}{marker}";

            var attachment = new AttachmentStorageService().AddAttachment(
                actor,
                AttachmentOwnerType.JobCard,
                jobCardId,
                AttachmentKind.JobPhoto,
                tempPath,
                notesWithMarker,
                string.IsNullOrWhiteSpace(originalFileName) ? null : originalFileName.Trim());

            var completionMessage = await ApplySubmissionJobCardUpdatesAsync(
                jobCardId,
                actor,
                gridLocation,
                cancellationToken);

            var importMessage = string.IsNullOrWhiteSpace(completionMessage)
                ? "Imported by desktop sync."
                : $"Imported by desktop sync. {completionMessage}";

            await MarkSubmissionImportedAsync(doc.Reference, attachment.Id, importMessage, cancellationToken);
            NotifyTechnicianDesktopEvents(jobCardId, technicianDisplay, verificationLabel, false, isFinalInBatch);
            return true;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }
    }

    private async Task<string?> ApplySubmissionJobCardUpdatesAsync(
        int jobCardId,
        string actor,
        string? gridLocation,
        CancellationToken cancellationToken)
    {
        using (var db = new AppDbContext())
        {
            var job = db.JobCards.FirstOrDefault(x => x.Id == jobCardId);
            if (job is null)
                return null;

            var normalizedGridLocation = string.IsNullOrWhiteSpace(gridLocation) ? null : gridLocation.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedGridLocation)
                && !string.Equals(job.GridLocation, normalizedGridLocation, StringComparison.Ordinal))
            {
                job.GridLocation = normalizedGridLocation;
                db.SaveChanges();
            }
        }

        if (!HasAllRequiredVerificationPhotos(jobCardId))
            return "Waiting for all required verification photos.";

        using var checkDb = new AppDbContext();
        var status = checkDb.JobCards
            .AsNoTracking()
            .Where(x => x.Id == jobCardId)
            .Select(x => (JobStatus?)x.Status)
            .FirstOrDefault();

        if (status != JobStatus.Open)
            return null;

        var workflow = new WorkflowService();
        var completeResult = await workflow.CompleteJobCardAsync(
            jobCardId,
            actor,
            _settings?.WialonApiToken);

        if (completeResult.ok)
        {
            var parts = JobCompletionNotificationParser.Parse(completeResult.message);
            _statusSink?.Invoke(
                $"Firebase sync auto-completed {GetJobReference(jobCardId)} after technician verification photos. {parts.PrimaryMessage}",
                false);

            foreach (var info in parts.IntegrationInfo)
            {
                _statusSink?.Invoke($"Integration: {info}", false);
            }

            foreach (var warning in parts.IntegrationWarnings)
            {
                _statusSink?.Invoke($"Integration warning: {warning}", true);
            }

            return parts.PrimaryMessage;
        }

        _statusSink?.Invoke(
            $"Firebase sync could not auto-complete {GetJobReference(jobCardId)}: {completeResult.message}",
            true);
        return $"Auto-complete failed: {completeResult.message}";
    }

    private static bool HasAllRequiredVerificationPhotos(int jobCardId)
    {
        using var db = new AppDbContext();
        var notes = db.Attachments
            .AsNoTracking()
            .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                        && a.OwnerId == jobCardId
                        && a.Kind == AttachmentKind.JobPhoto
                        && a.Notes != null)
            .Select(a => a.Notes!)
            .ToList();

        if (notes.Count == 0)
            return false;

        foreach (var marker in RequiredVerificationMarkers)
        {
            var markerFound = notes.Any(note => note.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (!markerFound)
                return false;
        }

        return true;
    }

    private static string GetJobReference(int jobCardId)
    {
        using var db = new AppDbContext();
        var info = db.JobCards
            .AsNoTracking()
            .Where(x => x.Id == jobCardId)
            .Select(x => new { x.JobCardNumber, x.Type })
            .FirstOrDefault();

        if (info is null)
            return $"job card {jobCardId}";

        return JobCardReferenceFormatter.Format(info.Type, info.JobCardNumber);
    }

    private static async Task MarkSubmissionImportedAsync(DocumentReference reference, int? localAttachmentId, string message, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["importStatus"] = "imported",
            ["importedAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow),
            ["importMessage"] = message,
            ["localAttachmentId"] = localAttachmentId
        };

        await reference.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
    }

    private static async Task MarkSubmissionFailedAsync(DocumentReference reference, string message, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["importStatus"] = "failed",
            ["importedAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow),
            ["importMessage"] = message
        };

        await reference.SetAsync(payload, SetOptions.MergeAll, cancellationToken);
    }

    private static (string bucket, string objectName) ParseStorageReference(string storagePath, string? defaultBucket)
    {
        var value = storagePath.Trim();
        if (value.StartsWith("gs://", StringComparison.OrdinalIgnoreCase))
        {
            var noScheme = value["gs://".Length..];
            var slash = noScheme.IndexOf('/');
            if (slash <= 0 || slash >= noScheme.Length - 1)
                throw new InvalidOperationException("Invalid Firebase storage path.");

            var bucket = noScheme[..slash];
            var objectName = noScheme[(slash + 1)..];
            return (bucket, objectName);
        }

        if (string.IsNullOrWhiteSpace(defaultBucket))
            throw new InvalidOperationException("Firebase bucket is required when storagePath does not contain bucket.");

        return (defaultBucket.Trim(), value.TrimStart('/'));
    }

    private void NotifyTechnicianDesktopEvents(
        int jobCardId,
        string technicianDisplay,
        string verificationLabel,
        bool duplicateSkipped,
        bool isFinalInBatch)
    {
        var jobReference = GetJobReference(jobCardId);
        if (duplicateSkipped)
        {
            _statusSink?.Invoke(
                $"Technician {technicianDisplay} re-submitted {verificationLabel} photo for {jobReference}. Duplicate skipped.",
                false);
        }
        else
        {
            _statusSink?.Invoke(
                $"Technician {technicianDisplay} uploaded {verificationLabel} photo for {jobReference}.",
                false);
        }

        if (isFinalInBatch)
        {
            _statusSink?.Invoke(
                $"Technician {technicianDisplay} pressed Save for {jobReference}.",
                false);
        }
    }

    private static string? ExtractVerificationMarker(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var markerStart = notes.IndexOf("[Verification:", StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0)
            return null;

        var markerEnd = notes.IndexOf(']', markerStart);
        if (markerEnd <= markerStart)
            return null;

        return notes.Substring(markerStart, markerEnd - markerStart + 1).Trim();
    }

    private static string DescribeVerificationMarker(string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker))
            return "verification";

        return marker.Trim().ToLowerInvariant() switch
        {
            "[verification:vehicle]" => "Vehicle",
            "[verification:registration]" => "Registration",
            "[verification:vin]" => "VIN",
            "[verification:trackingunit]" => "Tracking Unit",
            "[verification:serialiccid]" => "Serial/ICCID",
            _ => "verification"
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
            return 0;

        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is double d) return (int)d;
        if (raw is string s && int.TryParse(s, out var parsed)) return parsed;
        return 0;
    }

    private static bool GetBool(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
            return false;

        if (raw is bool b)
            return b;
        if (raw is string s && bool.TryParse(s, out var parsed))
            return parsed;
        if (raw is long l)
            return l != 0;
        if (raw is int i)
            return i != 0;

        return false;
    }

    private static string? GetString(IReadOnlyDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw.ToString();
    }

    private static string? ValidateSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.FirebaseProjectId))
            return "Firebase Project ID is required.";
        if (string.IsNullOrWhiteSpace(settings.FirebaseStorageBucket))
            return "Firebase Storage Bucket is required.";
        if (string.IsNullOrWhiteSpace(settings.FirebaseServiceAccountJsonPath))
            return "Firebase service account JSON path is required.";
        if (!File.Exists(settings.FirebaseServiceAccountJsonPath))
            return "Firebase service account JSON file was not found.";

        return null;
    }
}
