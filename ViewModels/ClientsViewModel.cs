using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ClientsViewModel : ViewModelBase
{
    private readonly AppState _appState;

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
        Load();
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

    partial void OnSearchTextChanged(string? value) => Load();

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var query = db.Clients.AsNoTracking().OrderBy(c => c.Name).AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            query = query.Where(c => c.Name.Contains(s));
        }

        Rows.Clear();
        foreach (var client in query.ToList())
        {
            Rows.Add(client);
        }

        _appState.SetStatus($"Loaded {Rows.Count} clients.");
    }

    [RelayCommand]
    private void NewClient()
    {
        SelectedRow = null;
        ClearFields();
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _appState.SetStatus("Client name is required.");
            return;
        }

        using var db = new AppDbContext();
        var normalizedName = Name.Trim();

        var selectedId = SelectedRow?.Id ?? 0;
        var existing = db.Clients.FirstOrDefault(c => c.Id == selectedId);
        var duplicate = db.Clients.AsNoTracking().FirstOrDefault(c => c.Name.ToLower() == normalizedName.ToLower());

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
        Load();
        SelectedRow = Rows.FirstOrDefault(c => string.Equals(c.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        _appState.SetStatus("Client saved.");
    }

    [RelayCommand]
    private void Delete()
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
        Load();
        _appState.SetStatus("Client deleted.");
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
