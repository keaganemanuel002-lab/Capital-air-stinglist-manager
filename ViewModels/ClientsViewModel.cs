using System;
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
    private readonly AppState _appState;
    private readonly IDataStore _dataStore;
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
        _ = Load();
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
    private async Task Load()
    {
        await LoadRowsAsync();
        _appState.SetStatus($"Loaded {Rows.Count} clients.");
    }

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
        await LoadRowsAsync();
        _appState.SetStatus(result.Message);

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
        await LoadRowsAsync();
        _appState.SetStatus("Client deleted.");
    }

    private async Task LoadRowsAsync(CancellationToken cancellationToken = default)
    {
        _searchLoadCts?.Cancel();
        _searchLoadCts?.Dispose();
        _searchLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _searchLoadCts.Token;
        try
        {
            var rows = await _dataStore.GetClientsAsync(SearchText, activeWialonClientKeys: null, token);

            Rows.Clear();
            foreach (var client in rows)
            {
                Rows.Add(client);
            }
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
            await LoadRowsAsync();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading clients: {ex.Message}", true);
        }
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
