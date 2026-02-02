using System;
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

public partial class StingListRow : ObservableObject
{
    public int Id { get; set; }
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Colour { get; set; }
    public string? VinNumber { get; set; }
    public string? TrackingUnitMake { get; set; }
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? SimNumber { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsArchived { get; set; }
}

public partial class StingListViewModel : PagedViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    public ObservableCollection<StingListRow> Rows { get; } = new();
    public ObservableCollection<FilterPreset> Presets { get; } = new();

    [ObservableProperty] private StingListRow? selectedRow;
    [ObservableProperty] private bool showArchived;
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private FilterPreset? selectedPreset;

    public StingListViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;

        // Load presets
        Presets.Clear();
        foreach (var p in _appState.Settings.StingPresets)
            Presets.Add(p);

        LoadPage();
    }

    public bool CanArchive => _appState.CanArchive;

    public bool CanStartRemoval
    {
        get => SelectedRow != null 
            && SelectedRow.Status != "Removed" 
            && !SelectedRow.IsArchived;
    }

    partial void OnShowArchivedChanged(bool value) => FirstPageCommand.Execute(null);
    partial void OnSearchTextChanged(string? value) => FirstPageCommand.Execute(null);
    partial void OnSelectedRowChanged(StingListRow? value) => OnPropertyChanged(nameof(CanStartRemoval));
    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        if (value is not null)
        {
            ShowArchived = value.ShowArchived;
            SearchText = value.CompanyContains;
        }
    }

    [RelayCommand]
    private void Refresh() => FirstPageCommand.Execute(null);

    protected override void LoadPage()
    {
        using var db = new AppDbContext();

        var q = db.BillingEntries.AsQueryable();

        if (!ShowArchived)
            q = q.Where(b => b.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            q = q.Where(x =>
                x.Company.Contains(s) ||
                x.Registration.Contains(s) ||
                (x.FleetNumber != null && x.FleetNumber.Contains(s)));
        }

        q = q.OrderByDescending(b => b.ActiveFrom);

        var items = q.Skip(Skip).Take(PageSize).ToList();

        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
        var logMsg = $"[LoadPage] Loaded {items.Count} items from BillingEntries";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

        Rows.Clear();
        foreach (var x in items)
        {
            logMsg = $"[LoadPage] BillingEntry {x.Id}: Company={x.Company}, Reg={x.Registration}, Make='{x.Make}', Model='{x.Model}', Imei='{x.Imei}', Iccid='{x.Iccid}'";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            Rows.Add(new StingListRow
            {
                Id = x.Id,
                Company = x.Company,
                Registration = x.Registration,
                FleetNumber = x.FleetNumber,
                Make = x.Make,
                Model = x.Model,
                Colour = x.Colour,
                VinNumber = x.VinNumber,
                TrackingUnitMake = x.TrackingUnitMake,
                Imei = x.Imei,
                SerialNumber = x.SerialNumber,
                Iccid = x.Iccid,
                SimNumber = x.SimNumber,
                Notes = x.Notes,
                Status = x.Status.ToString(),
                IsArchived = x.ArchivedAt != null
            });
        }

        _appState.SetStatus($"Loaded STING entries: page {PageNumber} ({Rows.Count} items, size {PageSize})");

        // Keep selection stable if possible
        if (SelectedRow != null)
        {
            SelectedRow = Rows.FirstOrDefault(r => r.Id == SelectedRow.Id);
        }
        
        // Notify that CanStartRemoval may have changed
        OnPropertyChanged(nameof(CanStartRemoval));
    }

    [RelayCommand]
    private async Task MarkRemoved()
    {
        if (!CanArchive) { _appState.SetStatus("Not permitted."); return; }
        if (SelectedRow is null) return;

        var ok = await DialogService.Confirm(
            _window,
            "Mark Removed",
            $"Mark this unit as REMOVED?\n\n{SelectedRow.Registration}\n\nThis will stop billing and set a removal date."
        );

        if (!ok) return;

        using var db = new AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (entry is null) return;

        entry.Status = BillingStatus.Removed;
        entry.ActiveTo = DateTime.UtcNow;

        db.SaveChanges();
        _appState.SetStatus("Unit marked as removed.");
        FirstPageCommand.Execute(null);
    }

    [RelayCommand]
    private async Task Archive()
    {
        if (!CanArchive) { _appState.SetStatus("Not permitted."); return; }
        if (SelectedRow is null) return;

        var ok = await DialogService.Confirm(
            _window,
            "Archive Entry",
            $"Archive this entry?\n\n{SelectedRow.Registration}\n\nIt will be hidden from the active billing list."
        );

        if (!ok) return;

        using var db = new AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (entry is null) return;

        entry.ArchivedAt = DateTime.UtcNow;

        db.SaveChanges();
        _appState.SetStatus("Entry archived.");
        FirstPageCommand.Execute(null);
    }

    [RelayCommand]
    private void StartRemoval()
    {
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
        System.IO.File.AppendAllText(logPath, "[StartRemoval] METHOD CALLED" + Environment.NewLine);
        
        if (SelectedRow is null)
        {
            System.IO.File.AppendAllText(logPath, "[StartRemoval] SelectedRow is NULL - returning" + Environment.NewLine);
            _appState.SetStatus("No entry selected.");
            return;
        }

        // Check if entry is already removed or archived
        if (SelectedRow.Status == "Removed" || SelectedRow.IsArchived)
        {
            var msg = "Cannot create removal request: This entry is already marked as removed or archived.";
            System.IO.File.AppendAllText(logPath, "[StartRemoval] " + msg + Environment.NewLine);
            _appState.SetStatus(msg);
            return;
        }

        var logMsg = $"[StartRemoval] SelectedRow data: Company={SelectedRow.Company}, Reg={SelectedRow.Registration}, Make={SelectedRow.Make}, Model={SelectedRow.Model}, Imei={SelectedRow.Imei}, SerialNumber={SelectedRow.SerialNumber}, Iccid={SelectedRow.Iccid}";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

        using var db = new AppDbContext();

        // Create removal quote prefilled from selected billing entry
        var quote = new Quote
        {
            Type = QuoteType.Removal,
            Status = QuoteStatus.Draft,
            Company = SelectedRow.Company,
            Registration = SelectedRow.Registration,
            FleetNumber = SelectedRow.FleetNumber,
            Make = SelectedRow.Make,
            Model = SelectedRow.Model,
            Colour = SelectedRow.Colour,
            VinNumber = SelectedRow.VinNumber,
            TrackingUnitMake = SelectedRow.TrackingUnitMake,
            Imei = SelectedRow.Imei,
            SerialNumber = SelectedRow.SerialNumber,
            Iccid = SelectedRow.Iccid,
            SimNumber = SelectedRow.SimNumber,
            AmountExVat = 0m,
            Notes = $"Removal for unit: {SelectedRow.Registration}"
        };

        logMsg = $"[StartRemoval] Quote created with: Make={quote.Make}, Model={quote.Model}, Imei={quote.Imei}, Iccid={quote.Iccid}, SerialNumber={quote.SerialNumber}";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

        db.Quotes.Add(quote);
        int changes = db.SaveChanges();
        
        logMsg = $"[StartRemoval] SaveChanges returned {changes}. Quote ID={quote.Id}";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
        
        logMsg = $"[StartRemoval] After save: Make={quote.Make}, Model={quote.Model}, Imei={quote.Imei}, Iccid={quote.Iccid}";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

        // Create linked cancellation entry so approval can proceed
        var cancellation = new CancellationEntry
        {
            Client = SelectedRow.Company,
            Registration = SelectedRow.Registration,
            FleetNumber = SelectedRow.FleetNumber,
            MakeModel = string.IsNullOrWhiteSpace(SelectedRow.Make) || string.IsNullOrWhiteSpace(SelectedRow.Model)
                ? null
                : $"{SelectedRow.Make} {SelectedRow.Model}",
            UnitModel = SelectedRow.TrackingUnitMake,
            DateRequestReceived = DateTime.UtcNow,
            Status = CancellationStatus.Quoted,
            QuoteId = quote.Id,
            Notes = "Created automatically from STING list removal request"
        };

        db.CancellationEntries.Add(cancellation);
        db.SaveChanges();
        
        logMsg = $"[StartRemoval] CancellationEntry created with ID={cancellation.Id} and linked to Quote {quote.Id}";
        System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
        
        _appState.SetStatus("Removal quote created with linked cancellation request. Navigate to Quotes to approve.");
    }
}
