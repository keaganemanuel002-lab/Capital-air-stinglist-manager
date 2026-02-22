using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ClientsViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoWialonSyncInterval = TimeSpan.FromMinutes(15);

    private readonly AppState _appState;
    private readonly IDataStore _dataStore;
    private WialonApiService? _wialonService;
    private string? _wialonTokenInUse;
    private HashSet<string>? _activeWialonClientKeys;
    private bool _isLoading;
    private CancellationTokenSource? _searchLoadCts;

    public ObservableCollection<Client> Rows { get; } = new();

    [ObservableProperty] private Client? selectedRow;
    [ObservableProperty] private string? searchText;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? contactPerson;
    [ObservableProperty] private string? phoneNumber;
    [ObservableProperty] private string? emailAddress;
    [ObservableProperty] private string? address;

    public ClientsViewModel(AppState appState)
    {
        _appState = appState;
        _dataStore = DataStoreFactory.Create(_appState.Settings);
        _activeWialonClientKeys = BuildWialonFilterKeysFromSettings();
        _ = LoadInternalAsync(forceSync: false);
    }

    public void SetStatus(string message) => _appState.SetStatus(message);

    partial void OnSelectedRowChanged(Client? value)
    {
        if (value == null)
        {
            ClearFields();
            return;
        }

        Name = value.Name;
        ContactPerson = value.ContactPerson;
        PhoneNumber = value.PhoneNumber;
        EmailAddress = value.EmailAddress;
        Address = value.Address;
    }

    partial void OnSearchTextChanged(string? value) => _ = LoadRowsForSearchAsync();

    [RelayCommand]
    private async Task Load() => await LoadInternalAsync(forceSync: false);

    [RelayCommand]
    private async Task SyncNow() => await LoadInternalAsync(forceSync: true);

    [RelayCommand]
    private void NewClient()
    {
        SelectedRow = null;
        ClearFields();
    }

    [RelayCommand]
    private async Task Save()
    {
        var result = await _dataStore.SaveClientAsync(
            SelectedRow?.Id,
            Name,
            ContactPerson,
            PhoneNumber,
            EmailAddress,
            Address);

        if (!result.Success)
        {
            _appState.SetStatus(result.Message, true);
            return;
        }

        var normalizedName = Name.Trim();
        await LoadRowsAsync(result.Message);
        SelectedRow = Rows.FirstOrDefault(c =>
            c.Id == result.Client?.Id
            || string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedRow == null)
            return;

        var deleted = await _dataStore.DeleteClientAsync(SelectedRow.Id);
        if (!deleted)
            return;

        SelectedRow = null;
        await LoadRowsAsync("Client deleted.");
    }

    private async Task LoadInternalAsync(bool forceSync)
    {
        if (_isLoading)
            return;

        _isLoading = true;

        try
        {
            var syncedCount = 0;
            string? syncError = null;
            var shouldSync = forceSync || ShouldAutoSyncNow();

            if (shouldSync)
            {
                try
                {
                    var syncResult = await SyncClientsFromWialonAsync();
                    syncedCount = syncResult.inserted;
                    _activeWialonClientKeys = syncResult.activeWialonClientKeys;

                    _appState.Settings.LastWialonClientNames = syncResult.activeWialonClientNames;
                    _appState.Settings.LastWialonClientsSyncUtc = DateTime.UtcNow;
                    _appState.SaveSettings();
                }
                catch (Exception ex)
                {
                    syncError = ex.Message;
                    _activeWialonClientKeys = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(syncError))
            {
                await LoadRowsAsync(statusMessage: null);
                _appState.SetStatus($"Loaded {Rows.Count} clients. Wialon sync failed: {syncError}");
                return;
            }

            if (shouldSync)
            {
                if (syncedCount > 0)
                {
                    await LoadRowsAsync(statusMessage: null);
                    _appState.SetStatus($"Loaded {Rows.Count} clients. Synced {syncedCount} account(s) from Wialon.");
                    return;
                }

                await LoadRowsAsync(statusMessage: null);
                _appState.SetStatus($"Loaded {Rows.Count} clients. Wialon sync is up to date.");
                return;
            }

            _activeWialonClientKeys ??= BuildWialonFilterKeysFromSettings();
            var lastSync = _appState.Settings.LastWialonClientsSyncUtc;
            if (lastSync is not null)
            {
                var lastSyncLocal = EnsureUtc(lastSync.Value).ToLocalTime();
                await LoadRowsAsync(statusMessage: null);
                _appState.SetStatus($"Loaded {Rows.Count} clients. Last Wialon sync: {lastSyncLocal:yyyy-MM-dd HH:mm}.");
                return;
            }

            await LoadRowsAsync(statusMessage: null);
            _appState.SetStatus($"Loaded {Rows.Count} clients.");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task<(int inserted, HashSet<string>? activeWialonClientKeys, List<string> activeWialonClientNames)> SyncClientsFromWialonAsync()
    {
        var token = _appState.Settings.WialonApiToken;
        if (string.IsNullOrWhiteSpace(token))
            return (0, BuildWialonFilterKeysFromSettings(), _appState.Settings.LastWialonClientNames);

        var normalizedToken = token.Trim();
        if (_wialonService is null || !string.Equals(_wialonTokenInUse, normalizedToken, StringComparison.Ordinal))
        {
            if (_wialonService is not null)
            {
                try
                {
                    await _wialonService.LogoutAndDisposeAsync();
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }

            _wialonService = new WialonApiService(normalizedToken);
            _wialonTokenInUse = normalizedToken;
        }

        var connected = await _wialonService.TestConnectionAsync();
        if (!connected)
        {
            throw new Exception(string.IsNullOrWhiteSpace(_wialonService.LastError)
                ? "failed to connect"
                : _wialonService.LastError);
        }

        var resources = await _wialonService.GetResourcesAsync();
        var wialonNames = resources.Values
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();
        var activeKeys = new HashSet<string>(
            wialonNames
                .Select(NormalizeComparableText)
                .Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.Ordinal);

        var inserted = await _dataStore.InsertMissingClientsAsync(wialonNames);

        return (inserted, activeKeys, wialonNames);
    }

    private async Task LoadRowsAsync(string? statusMessage, CancellationToken cancellationToken = default)
    {
        _searchLoadCts?.Cancel();
        _searchLoadCts?.Dispose();
        _searchLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _searchLoadCts.Token;
        try
        {
            var rows = await _dataStore.GetClientsAsync(SearchText, _activeWialonClientKeys, token);

            Rows.Clear();
            foreach (var client in rows)
            {
                Rows.Add(client);
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
                _appState.SetStatus(statusMessage);
        }
        catch (OperationCanceledException)
        {
            // Newer load superseded this one.
        }
    }

    private async Task LoadRowsForSearchAsync()
    {
        try
        {
            await LoadRowsAsync(statusMessage: null);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading clients: {ex.Message}", true);
        }
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

    private bool ShouldAutoSyncNow()
    {
        if (string.IsNullOrWhiteSpace(_appState.Settings.WialonApiToken))
            return false;

        var lastSync = _appState.Settings.LastWialonClientsSyncUtc;
        if (lastSync is null)
            return true;

        var elapsed = DateTime.UtcNow - EnsureUtc(lastSync.Value);
        return elapsed >= AutoWialonSyncInterval;
    }

    private HashSet<string>? BuildWialonFilterKeysFromSettings()
    {
        var names = _appState.Settings.LastWialonClientNames;
        if (names is null || names.Count == 0)
            return null;

        return new HashSet<string>(
            names
                .Select(NormalizeComparableText)
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.Ordinal);
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

    private void ClearFields()
    {
        Name = "";
        ContactPerson = null;
        PhoneNumber = null;
        EmailAddress = null;
        Address = null;
    }
}
