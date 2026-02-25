using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using StingListManager.Data;

namespace StingListManager.Services;

public sealed class MongoSyncService : IDisposable
{
    private static readonly Lazy<MongoSyncService> LazyInstance = new(() => new MongoSyncService());
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(8);
    private static readonly Dictionary<Type, PropertyInfo?> IdPropertyCache = new();
    private static readonly object IdPropertyCacheLock = new();

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private Action<string, bool>? _statusSink;
    private AppSettings? _settings;
    private IMongoDatabase? _database;
    private DateTime _lastSyncedDbWriteUtc = DateTime.MinValue;
    private string? _lastErrorMessage;
    private string? _lastBackupErrorMessage;
    private bool _documentsBackupAnnounced;
    private bool _disposed;

    public static MongoSyncService Instance => LazyInstance.Value;
    public bool IsRunning => _workerTask is { IsCompleted: false };

    private MongoSyncService()
    {
    }

    public async Task<(bool started, string message)> StartAsync(AppSettings settings, Action<string, bool>? statusSink = null)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_disposed)
                return (false, "Mongo sync service is disposed.");

            _settings = settings;
            _statusSink = statusSink;

            if (IsRunning)
                return (true, "Mongo sync is already running.");

            if (!settings.MongoPrimaryDataEnabled)
                return (false, "Mongo sync is disabled.");

            if (string.IsNullOrWhiteSpace(settings.MongoConnectionString))
                return (false, "Mongo sync requires a MongoDB connection string.");

            _database = null;
            _lastErrorMessage = null;
            _lastBackupErrorMessage = null;
            _documentsBackupAnnounced = false;
            _lastSyncedDbWriteUtc = DateTime.MinValue;

            _cts = new CancellationTokenSource();
            _workerTask = RunLoopAsync(_cts.Token);
            return (true, "Mongo sync started.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_cts is null)
                return;

            _cts.Cancel();
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
            _database = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = StopAsync();
        _lifecycleLock.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var firstSync = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connected = await EnsureMongoConnectedAsync(cancellationToken);
                if (connected)
                {
                    var dbWriteUtc = GetLocalDatabaseWriteUtc();
                    if (firstSync || dbWriteUtc > _lastSyncedDbWriteUtc)
                    {
                        var mirroredRows = await MirrorAllCollectionsAsync(cancellationToken);
                        _lastSyncedDbWriteUtc = dbWriteUtc;
                        _lastErrorMessage = null;
                        CreateDocumentsBackupSnapshot();

                        if (firstSync || mirroredRows > 0)
                            _statusSink?.Invoke($"Mongo sync complete ({mirroredRows} records mirrored).", false);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ReportError($"Mongo sync failed: {ex.Message}");
            }

            firstSync = false;

            try
            {
                await Task.Delay(SyncInterval, cancellationToken);
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

    private async Task<bool> EnsureMongoConnectedAsync(CancellationToken cancellationToken)
    {
        if (_database is not null)
            return true;

        if (_settings is null || string.IsNullOrWhiteSpace(_settings.MongoConnectionString))
            return false;

        try
        {
            var dbName = string.IsNullOrWhiteSpace(_settings.MongoDatabaseName)
                ? "stinglistmanager"
                : _settings.MongoDatabaseName!.Trim();

            var client = new MongoClient(_settings.MongoConnectionString);
            var db = client.GetDatabase(dbName);
            await db.RunCommandAsync((Command<BsonDocument>)"{ping:1}", cancellationToken: cancellationToken);
            _database = db;
            return true;
        }
        catch (Exception ex)
        {
            _database = null;
            ReportError($"Mongo connection failed: {ex.Message}");
            return false;
        }
    }

    private async Task<int> MirrorAllCollectionsAsync(CancellationToken cancellationToken)
    {
        if (_database is null)
            return 0;

        using var db = new AppDbContext();
        var total = 0;

        total += await MirrorCollectionAsync("user_accounts", db.UserAccounts.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("clients", db.Clients.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("quotes", db.Quotes.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("quote_line_items", db.QuoteLineItems.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("job_cards", db.JobCards.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("cancellation_entries", db.CancellationEntries.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("billing_entries", db.BillingEntries.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("attachments", db.Attachments.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("audit_events", db.AuditEvents.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("client_quote_summaries", db.ClientQuoteSummaries.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("dashcams", db.Dashcams.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("sd_cards", db.SdCards.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("phone_issue_log_entries", db.PhoneIssueLogEntries.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("driver_tags", db.DriverTags.AsNoTracking(), cancellationToken);
        total += await MirrorCollectionAsync("driver_tag_transfers", db.DriverTagTransfers.AsNoTracking(), cancellationToken);

        return total;
    }

    private async Task<int> MirrorCollectionAsync<TEntity>(
        string collectionName,
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (_database is null)
            return 0;

        var rows = await query.ToListAsync(cancellationToken);
        var collection = _database.GetCollection<BsonDocument>(collectionName);
        var now = DateTime.UtcNow;

        var upserts = new List<WriteModel<BsonDocument>>(rows.Count);
        var localIds = new HashSet<BsonValue>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = GetEntityId(row);
            if (id <= 0)
                continue;

            var doc = row.ToBsonDocument();
            doc["_id"] = id;
            doc["syncedAtUtc"] = now;

            upserts.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", id),
                doc)
            {
                IsUpsert = true
            });

            localIds.Add(new BsonInt32(id));
        }

        if (upserts.Count > 0)
        {
            await collection.BulkWriteAsync(
                upserts,
                new BulkWriteOptions { IsOrdered = false },
                cancellationToken);
        }

        var remoteIdDocs = await collection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Project(Builders<BsonDocument>.Projection.Include("_id"))
            .ToListAsync(cancellationToken);

        var staleRemoteIds = remoteIdDocs
            .Select(x => x.TryGetValue("_id", out var id) ? id : null)
            .Where(x => x is not null && !localIds.Contains(x))
            .Cast<BsonValue>()
            .ToList();

        if (staleRemoteIds.Count > 0)
        {
            await collection.DeleteManyAsync(
                Builders<BsonDocument>.Filter.In("_id", staleRemoteIds),
                cancellationToken);
        }

        return rows.Count;
    }

    private static int GetEntityId<TEntity>(TEntity row)
        where TEntity : class
    {
        var type = typeof(TEntity);
        PropertyInfo? idProperty;
        lock (IdPropertyCacheLock)
        {
            if (!IdPropertyCache.TryGetValue(type, out idProperty))
            {
                idProperty = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                IdPropertyCache[type] = idProperty;
            }
        }

        if (idProperty is null)
            return 0;

        var value = idProperty.GetValue(row);
        return value is int id ? id : 0;
    }

    private static DateTime GetLocalDatabaseWriteUtc()
    {
        try
        {
            var dbUtc = File.Exists(Paths.DbPath)
                ? File.GetLastWriteTimeUtc(Paths.DbPath)
                : DateTime.MinValue;

            var walPath = $"{Paths.DbPath}-wal";
            var walUtc = File.Exists(walPath)
                ? File.GetLastWriteTimeUtc(walPath)
                : DateTime.MinValue;

            return dbUtc >= walUtc ? dbUtc : walUtc;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private void CreateDocumentsBackupSnapshot()
    {
        var actor = string.IsNullOrWhiteSpace(_settings?.OperatorName)
            ? Environment.UserName
            : _settings!.OperatorName;

        try
        {
            var path = new BackupService().CreateDocumentsBackup(actor);
            _lastBackupErrorMessage = null;

            if (!_documentsBackupAnnounced)
            {
                _documentsBackupAnnounced = true;
                _statusSink?.Invoke($"Local backup saved in Documents: {path}", false);
            }
        }
        catch (Exception ex)
        {
            if (string.Equals(_lastBackupErrorMessage, ex.Message, StringComparison.Ordinal))
                return;

            _lastBackupErrorMessage = ex.Message;
            _statusSink?.Invoke($"Documents backup failed: {ex.Message}", true);
        }
    }

    private void ReportError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (string.Equals(_lastErrorMessage, message, StringComparison.Ordinal))
            return;

        _lastErrorMessage = message;
        _statusSink?.Invoke(message, true);
    }
}
