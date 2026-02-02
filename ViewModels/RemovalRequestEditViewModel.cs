using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class RemovalRequestEditViewModel : ViewModelBase
{
    private readonly int? _id;
    private readonly Action _close;
    private bool _isLoading;

    [ObservableProperty] private string? client;
    [ObservableProperty] private string? registration;
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? makeModel;
    [ObservableProperty] private DateTimeOffset? dateReceived = DateTimeOffset.Now.Date;
    [ObservableProperty] private string? reason;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? errorMessage;

    public ObservableCollection<string> AvailableClients { get; } = new();
    public ObservableCollection<string> AvailableRegistrations { get; } = new();
    public ObservableCollection<string> AvailableFleetNumbers { get; } = new();

    public RemovalRequestEditViewModel(int? id, Action close)
    {
        _id = id;
        _close = close;

        // Load all available values from STING List
        LoadAvailableValues();

        if (id is null) return;

        using var db = new AppDbContext();
        var c = db.CancellationEntries.Find(id.Value);
        if (c is null) return;

        _isLoading = true;
        Client = c.Client;
        Registration = c.Registration;
        FleetNumber = c.FleetNumber;
        MakeModel = c.MakeModel;
        Reason = c.Reason;
        Notes = c.Notes;
        DateReceived = c.DateRequestReceived is null ? DateTimeOffset.Now.Date : new DateTimeOffset(c.DateRequestReceived.Value.Date);
        _isLoading = false;

        // Load filtered data for the selected registration
        if (!string.IsNullOrWhiteSpace(Registration))
            OnRegistrationSelected();
    }

    private void LoadAvailableValues()
    {
        using var db = new AppDbContext();
        var activeEntries = db.BillingEntries
            .Where(b => b.Status == BillingStatus.Active && b.ArchivedAt == null)
            .ToList();

        // Get unique clients
        var clients = activeEntries
            .Select(b => b.Company)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        AvailableClients.Clear();
        foreach (var c in clients)
            AvailableClients.Add(c);

        // Get unique registrations
        var regs = activeEntries
            .Select(b => b.Registration)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        AvailableRegistrations.Clear();
        foreach (var r in regs)
            AvailableRegistrations.Add(r);
    }

    partial void OnRegistrationChanged(string? value)
    {
        if (_isLoading) return;
        OnRegistrationSelected();
    }

    private void OnRegistrationSelected()
    {
        AvailableFleetNumbers.Clear();

        if (string.IsNullOrWhiteSpace(Registration))
            return;

        var reg = Registration.Trim().ToUpperInvariant();

        using var db = new AppDbContext();
        var entries = db.BillingEntries
            .Where(b => b.RegistrationNorm == reg && 
                       b.Status == BillingStatus.Active && 
                       b.ArchivedAt == null)
            .ToList();

        if (!entries.Any())
            return;

        // Get unique fleet numbers for this registration
        var fleetNums = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.FleetNumber))
            .Select(e => e.FleetNumber!)
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        foreach (var f in fleetNums)
            AvailableFleetNumbers.Add(f);

        // Auto-populate MakeModel from first entry if not set
        if (string.IsNullOrWhiteSpace(MakeModel) && entries.Any())
        {
            var first = entries.First();
            var makeModel = string.Join(" ", new[] { first.Make, first.Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
            MakeModel = string.IsNullOrWhiteSpace(makeModel) ? null : makeModel;
        }

        // Auto-select first fleet number if only one and not set
        if (string.IsNullOrWhiteSpace(FleetNumber) && AvailableFleetNumbers.Count == 1)
            FleetNumber = AvailableFleetNumbers[0];
    }

    [RelayCommand] private void Cancel() => _close();

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Client)) { ErrorMessage = "Client is required."; return; }
        if (string.IsNullOrWhiteSpace(Registration)) { ErrorMessage = "Registration is required."; return; }

        using var db = new AppDbContext();

        CancellationEntry c;
        if (_id is null)
        {
            c = new CancellationEntry { Status = CancellationStatus.Requested };
            db.CancellationEntries.Add(c);
        }
        else
        {
            c = db.CancellationEntries.Find(_id.Value) ?? new CancellationEntry { Status = CancellationStatus.Requested };
            if (c.Id == 0) db.CancellationEntries.Add(c);
        }

        c.Client = Client.Trim();
        c.Registration = Registration.Trim().ToUpperInvariant();
        c.FleetNumber = string.IsNullOrWhiteSpace(FleetNumber) ? null : FleetNumber.Trim();
        c.MakeModel = string.IsNullOrWhiteSpace(MakeModel) ? null : MakeModel.Trim();
        c.Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
        c.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        c.DateRequestReceived = DateReceived?.DateTime.Date;

        db.SaveChanges();
        _close();
    }
}
