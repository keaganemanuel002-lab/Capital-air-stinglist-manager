using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ClientsViewModel : ViewModelBase
{
    private static readonly TimeSpan AutoWialonSyncInterval = TimeSpan.FromMinutes(15);

    private readonly AppState _appState;
    private WialonApiService? _wialonService;
    private string? _wialonTokenInUse;
    private HashSet<string>? _activeWialonClientKeys;
    private bool _isLoading;

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

    partial void OnSearchTextChanged(string? value) => LoadLocalRows();

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
        if (string.IsNullOrWhiteSpace(Name))
        {
            _appState.SetStatus("Client name is required.");
            return;
        }

        using var db = new AppDbContext();
        var normalizedName = Name.Trim();
        var normalizedComparableName = NormalizeComparableText(normalizedName);

        var selectedId = SelectedRow?.Id ?? 0;
        var existing = db.Clients.FirstOrDefault(c => c.Id == selectedId);
        var duplicate = db.Clients
            .AsNoTracking()
            .FirstOrDefault(c => c.NameNorm == normalizedComparableName);

        if (existing == null && duplicate != null)
        {
            _appState.SetStatus("Client name already exists.");
            return;
        }

        if (existing == null)
        {
            existing = new Client
            {
                Name = normalizedName,
                ContactPerson = ContactPerson?.Trim(),
                PhoneNumber = PhoneNumber?.Trim(),
                EmailAddress = EmailAddress?.Trim(),
                Address = Address?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            db.Clients.Add(existing);
        }
        else
        {
            existing.Name = normalizedName;
            existing.ContactPerson = ContactPerson?.Trim();
            existing.PhoneNumber = PhoneNumber?.Trim();
            existing.EmailAddress = EmailAddress?.Trim();
            existing.Address = Address?.Trim();
        }

        db.SaveChanges();
        await LoadInternalAsync(forceSync: false);
        SelectedRow = Rows.FirstOrDefault(c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        _appState.SetStatus("Client saved.");
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedRow == null)
            return;

        using var db = new AppDbContext();
        var client = db.Clients.FirstOrDefault(c => c.Id == SelectedRow.Id);
        if (client == null)
            return;

        db.Clients.Remove(client);
        db.SaveChanges();

        SelectedRow = null;
        await LoadInternalAsync(forceSync: false);
        _appState.SetStatus("Client deleted.");
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

            _activeWialonClientKeys ??= BuildWialonFilterKeysFromSettings();
            LoadLocalRows();

            if (!string.IsNullOrWhiteSpace(syncError))
            {
                _appState.SetStatus($"Loaded {Rows.Count} clients. Wialon sync failed: {syncError}");
                return;
            }

            if (shouldSync)
            {
                if (syncedCount > 0)
                {
                    _appState.SetStatus($"Loaded {Rows.Count} clients. Synced {syncedCount} account(s) from Wialon.");
                    return;
                }

                _appState.SetStatus($"Loaded {Rows.Count} clients. Wialon sync is up to date.");
                return;
            }

            var lastSync = _appState.Settings.LastWialonClientsSyncUtc;
            if (lastSync is not null)
            {
                var lastSyncLocal = EnsureUtc(lastSync.Value).ToLocalTime();
                _appState.SetStatus($"Loaded {Rows.Count} clients. Last Wialon sync: {lastSyncLocal:yyyy-MM-dd HH:mm}.");
                return;
            }

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();
        var activeKeys = new HashSet<string>(
            wialonNames
                .Select(NormalizeComparableText)
                .Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.Ordinal);

        using var db = new AppDbContext();
        var existingKeys = new HashSet<string>(
            db.Clients
                .AsNoTracking()
                .Select(c => c.Name)
                .AsEnumerable()
                .Select(NormalizeComparableText)
                .Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.Ordinal);

        var inserted = 0;
        foreach (var name in wialonNames)
        {
            var normalizedName = NormalizeComparableText(name);
            if (string.IsNullOrWhiteSpace(normalizedName) || existingKeys.Contains(normalizedName))
                continue;

            db.Clients.Add(new Client
            {
                Name = name!,
                CreatedAt = DateTime.UtcNow
            });
            existingKeys.Add(normalizedName);
            inserted++;
        }

        if (inserted > 0)
        {
            db.SaveChanges();
        }

        return (inserted, activeKeys, wialonNames.Select(x => x!).ToList());
    }

    private void LoadLocalRows()
    {
        using var db = new AppDbContext();
        var query = db.Clients.AsNoTracking().OrderBy(c => c.Name).AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            query = query.Where(c => c.Name.Contains(s));
        }

        var rows = query.ToList();
        if (_activeWialonClientKeys is not null)
        {
            rows = rows
                .Where(c => _activeWialonClientKeys.Contains(NormalizeComparableText(c.Name)))
                .ToList();
        }

        Rows.Clear();
        foreach (var client in rows)
        {
            Rows.Add(client);
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
