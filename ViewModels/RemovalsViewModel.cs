using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class RemovalRow : ObservableObject
{
    public int Id { get; set; }
    public string Client { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string? MakeModel { get; set; }
    public string Status { get; set; } = "";
    public string DateReceived { get; set; } = "";
    public string? Reason { get; set; }
}

public partial class RemovalsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    public ObservableCollection<RemovalRow> Rows { get; } = new();

    [ObservableProperty] private RemovalRow? selectedRow;

    public List<string> StatusOptions { get; } = new();

    [ObservableProperty] private string selectedStatus = "All";
    [ObservableProperty] private string? clientFilter;
    [ObservableProperty] private string? registrationFilter;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;

    public RemovalsViewModel(Window window, AppState appState, DateTime? startDate = null, DateTime? endDate = null)
    {
        _window = window;
        _appState = appState;
        StatusOptions.Add("All");
        StatusOptions.AddRange(Enum.GetNames(typeof(CancellationStatus)));
        SetDefaultDateRange(startDate, endDate);
        Load();
    }

    partial void OnSelectedStatusChanged(string value) => Load();
    partial void OnClientFilterChanged(string? value) => Load();
    partial void OnRegistrationFilterChanged(string? value) => Load();
    partial void OnStartDateChanged(DateTimeOffset? value) => Load();
    partial void OnEndDateChanged(DateTimeOffset? value) => Load();

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();

        var query = db.CancellationEntries.AsQueryable();

        if (!string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<CancellationStatus>(SelectedStatus, out var status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(ClientFilter))
        {
            var s = ClientFilter.Trim();
            query = query.Where(c => c.Client.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(RegistrationFilter))
        {
            var s = RegistrationFilter.Trim();
            query = query.Where(c => c.Registration.Contains(s));
        }

        if (StartDate != null)
        {
            var start = StartDate.Value.Date;
            query = query.Where(c => c.DateRequestReceived != null && c.DateRequestReceived >= start);
        }

        if (EndDate != null)
        {
            var endExclusive = EndDate.Value.Date.AddDays(1);
            query = query.Where(c => c.DateRequestReceived != null && c.DateRequestReceived < endExclusive);
        }

        var items = query
            .OrderByDescending(c => c.DateRequestReceived)
            .ThenByDescending(c => c.Id)
            .ToList();

        Rows.Clear();
        foreach (var c in items)
        {
            Rows.Add(new RemovalRow
            {
                Id = c.Id,
                Client = c.Client,
                Registration = c.Registration,
                FleetNumber = c.FleetNumber,
                MakeModel = c.MakeModel,
                Status = c.Status.ToString(),
                DateReceived = c.DateRequestReceived?.ToString("yyyy-MM-dd") ?? "",
                Reason = c.Reason
            });
        }

        _appState.SetStatus($"Loaded {Rows.Count} removal requests.");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedStatus = "All";
        ClientFilter = null;
        RegistrationFilter = null;
        SetDefaultDateRange(null, null);
        Load();
    }

    private void SetDefaultDateRange(DateTime? start, DateTime? end)
    {
        if (start != null || end != null)
        {
            StartDate = start != null ? new DateTimeOffset(start.Value.Date) : null;
            EndDate = end != null ? new DateTimeOffset(end.Value.Date) : null;
            return;
        }

        var today = DateTime.Today;
        StartDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        EndDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));
    }

    [RelayCommand]
    private async Task AddRequest()
    {
        var dlg = new StingListManager.Views.RemovalRequestEditWindow();
        dlg.DataContext = new RemovalRequestEditViewModel(null, () => dlg.Close());

        await dlg.ShowDialog(_window);
        Load();
        _appState.SetStatus("Removal request added.");
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.RemovalRequestEditWindow();
        dlg.DataContext = new RemovalRequestEditViewModel(SelectedRow.Id, () => dlg.Close());

        await dlg.ShowDialog(_window);
        Load();
        _appState.SetStatus("Removal request updated.");
    }

    [RelayCommand]
    private async Task CreateRemovalQuote()
    {
        if (SelectedRow is null) return;

        using var db = new AppDbContext();
        var c = db.CancellationEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (c is null) return;

        var activeEntry = db.BillingEntries
            .Where(b => b.ArchivedAt == null
                        && (b.Status == BillingStatus.Active || b.Status == BillingStatus.NotLoaded)
                        && b.Company == c.Client
                        && b.Registration == c.Registration)
            .OrderByDescending(b => b.ActiveFrom)
            .FirstOrDefault();

        var createRemovalJobCard = true;
        if (activeEntry is not null && !WarrantyService.IsWithinWarranty(activeEntry.ActiveFrom))
        {
            createRemovalJobCard = await DialogService.Confirm(
                _window,
                "Out of Warranty Removal",
                "This unit is out of warranty.\n\nCreate a removal job card?\n\nYes = create removal job card when quote is approved\nNo = approve removal quote without creating a job card");
        }

        var removalNotes = $"Removal request for {c.Registration}";
        if (!createRemovalJobCard)
            removalNotes = $"{removalNotes} {WorkflowService.NoRemovalJobCardMarker}";

        // Create removal quote linked to this cancellation
        var q = new Quote
        {
            QuoteNumber = QuoteNumberAllocator.GetNext(db),
            Type = QuoteType.Removal,
            Status = QuoteStatus.Draft,
            Company = c.Client,
            Registration = c.Registration,
            FleetNumber = c.FleetNumber,
            Make = activeEntry?.Make,
            Model = activeEntry?.Model,
            Colour = activeEntry?.Colour,
            VinNumber = activeEntry?.VinNumber,
            TrackingUnitMake = activeEntry?.TrackingUnitMake,
            Imei = activeEntry?.Imei,
            SerialNumber = activeEntry?.SerialNumber,
            Iccid = activeEntry?.Iccid,
            SimNumber = activeEntry?.SimNumber,
            AmountExVat = _appState.Settings.DefaultRemovalFeeExVat,
            Notes = removalNotes
        };

        q.LineItems.Add(new QuoteLineItem
        {
            LineNumber = 1,
            ProductType = "Removal Fee",
            ProductCode = "AUTO-REMOVAL-FEE",
            ProductName = "Removal Fee",
            Quantity = 1,
            UnitPriceExVat = _appState.Settings.DefaultRemovalFeeExVat,
            LineTotalExVat = _appState.Settings.DefaultRemovalFeeExVat,
            IsVatExempt = false,
            Description = "Auto-added removal fee"
        });

        db.Quotes.Add(q);
        db.SaveChanges();

        c.QuoteId = q.Id;
        c.Status = CancellationStatus.Quoted;
        db.SaveChanges();

        var quoteReference = QuoteReferenceFormatter.Format(q.QuoteNumber);
        var message = createRemovalJobCard
            ? $"Removal quote {quoteReference} created (Draft). Approve it under Quotes to create a removal job card."
            : $"Removal quote {quoteReference} created (Draft). It is marked to approve without a job card.";
        _appState.SetStatus(message);
        Load();
    }
}
