using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class BillingListRow : ObservableObject
{
    public int Id { get; set; }
    public string RowLabel { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string PackageType { get; set; } = "";
    public string VehicleDescription { get; set; } = "";
    public string Code { get; set; } = "";
    public string PackageCharge { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool? LiveTrackingEnabled { get; set; }
    public bool IsClientSummaryRow { get; set; }
    public IBrush RowBackground { get; set; } = Brushes.White;
    public IBrush RowForeground { get; set; } = Brushes.Black;
    public FontWeight RowFontWeight { get; set; } = FontWeight.Normal;
}

public partial class BillingListViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly IDataStore _dataStore;
    private static readonly IBrush ClientSummaryRowBackground = new SolidColorBrush(Color.Parse("#EAF0FB"));
    private static readonly IBrush ClientSummaryRowForeground = new SolidColorBrush(Color.Parse("#1E3A8A"));
    private bool _suppressFilterReload;

    public ObservableCollection<BillingListRow> Rows { get; } = new();
    public ObservableCollection<string> AvailableClients { get; } = new();

    [ObservableProperty] private BillingListRow? selectedRow;
    [ObservableProperty] private List<BillingListRow>? selectedRows;
    [ObservableProperty] private string selectedClient = "All";
    [ObservableProperty] private string? registrationSearch;

    public bool CanEditSelectedRow =>
        ResolvePrimarySelectedRow() is { IsClientSummaryRow: false, Id: > 0 };
    public bool CanDeleteSelectedRow =>
        _appState.CanArchive && ResolvePrimarySelectedRow() is { IsClientSummaryRow: false, Id: > 0 };

    public BillingListViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        _dataStore = DataStoreFactory.Create(_appState.Settings);
        _ = Load();
    }

    partial void OnSelectedClientChanged(string value)
    {
        if (_suppressFilterReload)
            return;

        _ = Load();
    }

    partial void OnSelectedRowChanged(BillingListRow? value)
    {
        NotifySelectionStateChanged();
    }

    partial void OnSelectedRowsChanged(List<BillingListRow>? value)
    {
        NotifySelectionStateChanged();
    }

    partial void OnRegistrationSearchChanged(string? value)
    {
        if (_suppressFilterReload)
            return;

        _ = Load();
    }

    [RelayCommand]
    private async Task Load()
    {
        var selectedBeforeReload = ResolvePrimarySelectedRow();
        var selectedRowIdBeforeReload = selectedBeforeReload is { IsClientSummaryRow: false, Id: > 0 }
            ? selectedBeforeReload.Id
            : (int?)null;
        var selectedRowIndexBeforeReload = selectedBeforeReload is null
            ? -1
            : Rows.IndexOf(selectedBeforeReload);

        List<BillingEntry> allEntries;
        try
        {
            allEntries = await _dataStore.GetActiveBillingEntriesAsync();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not load billing list: {ex.Message}", true);
            return;
        }

        RefreshClientOptions(allEntries);

        IEnumerable<BillingEntry> filtered = allEntries;
        if (!string.Equals(SelectedClient, "All", StringComparison.OrdinalIgnoreCase))
        {
            var selectedClientName = NormalizeClientDisplayName(SelectedClient);
            filtered = filtered.Where(e =>
                string.Equals(
                    NormalizeClientDisplayName(e.Company),
                    selectedClientName,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(RegistrationSearch))
        {
            var search = RegistrationSearch.Trim();
            filtered = filtered.Where(e =>
                !string.IsNullOrWhiteSpace(e.Registration)
                && e.Registration.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var entries = filtered
            .OrderBy(e => e.Company)
            .ThenBy(e => e.Registration)
            .ToList();

        Dictionary<string, bool> liveTrackingByClient;
        using (var db = new AppDbContext())
        {
            liveTrackingByClient = db.ClientQuoteSummaries
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.Company))
                .ToList()
                .GroupBy(x => NormalizeClientDisplayName(x.Company), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.UpdatedAt).First().HasLiveTracking,
                    StringComparer.OrdinalIgnoreCase);
        }

        Rows.Clear();

        var grouped = entries.GroupBy(e => e.Company).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var clientEntries = group.ToList();
            var rowNumber = 1;

            foreach (var entry in clientEntries)
            {
                Rows.Add(new BillingListRow
                {
                    Id = entry.Id,
                    RowLabel = rowNumber.ToString(),
                    Company = entry.Company,
                    Registration = entry.Registration,
                    FleetNumber = entry.FleetNumber,
                    PackageType = entry.StingPackageType ?? ResolvePackageLabel(entry),
                    VehicleDescription = BuildVehicleDescription(entry),
                    Code = BuildCode(entry),
                    PackageCharge = "",
                    Notes = entry.Notes ?? "",
                    Reason = entry.Reason ?? "",
                    IsClientSummaryRow = false,
                    RowBackground = Brushes.White,
                    RowForeground = Brushes.Black,
                    RowFontWeight = FontWeight.Normal
                });

                rowNumber++;
            }

            var packageSummary = BuildPackageSummaryFromEntries(group.Key, clientEntries);
            var totalUnits = clientEntries.Count;
            var clientKey = NormalizeClientDisplayName(group.Key);
            var liveTrackingEnabled = liveTrackingByClient.TryGetValue(clientKey, out var configuredState)
                ? configuredState
                : totalUnits > 0;
            var liveTrackingUnits = liveTrackingEnabled ? totalUnits : 0;
            var packageTotal = packageSummary.StingCount + packageSummary.StingPlusCount + packageSummary.StingFmCount;
            var packageSourceNote = packageTotal <= 0
                ? "Package mix unavailable (set package type to STING/STING PLUS/STING FM)"
                : "Package mix from active STING list entries";
            var liveTrackingNote = liveTrackingEnabled
                ? "Live tracking enabled."
                : "Live tracking disabled.";

            Rows.Add(new BillingListRow
            {
                Id = 0,
                RowLabel = "TOTAL",
                Company = group.Key,
                Registration = $"{totalUnits} units",
                FleetNumber = $"{liveTrackingUnits} live",
                PackageType = "TOTAL",
                VehicleDescription =
                    $"STING {packageSummary.StingCount} | STING PLUS {packageSummary.StingPlusCount} | STING FM {packageSummary.StingFmCount}",
                Code = string.Empty,
                PackageCharge = "CLIENT TOTAL",
                Notes = $"{liveTrackingNote} {packageSourceNote}",
                Reason = "",
                LiveTrackingEnabled = liveTrackingEnabled,
                IsClientSummaryRow = true,
                RowBackground = ClientSummaryRowBackground,
                RowForeground = ClientSummaryRowForeground,
                RowFontWeight = FontWeight.Bold
            });
        }

        BillingListRow? restoredSelection = null;
        if (selectedRowIdBeforeReload is int selectedId)
        {
            restoredSelection = Rows.FirstOrDefault(row =>
                !row.IsClientSummaryRow && row.Id == selectedId);
        }

        if (restoredSelection is null && selectedRowIndexBeforeReload >= 0)
        {
            restoredSelection = FindNearestDataRow(selectedRowIndexBeforeReload);
        }

        SelectedRow = restoredSelection;
        SelectedRows = restoredSelection is null
            ? null
            : new List<BillingListRow> { restoredSelection };

        _appState.SetStatus($"Loaded billing list: {entries.Count} entries across {grouped.Count()} clients ({Rows.Count} rows incl. totals).");
        NotifySelectionStateChanged();
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        _suppressFilterReload = true;
        try
        {
            SelectedClient = "All";
            RegistrationSearch = null;
        }
        finally
        {
            _suppressFilterReload = false;
        }

        await Load();
    }

    private void RefreshClientOptions(IReadOnlyCollection<BillingEntry> entries)
    {
        var selectedBeforeRefresh = NormalizeClientDisplayName(SelectedClient);
        var clients = entries
            .Select(e => NormalizeClientDisplayName(e.Company))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        _suppressFilterReload = true;
        try
        {
            AvailableClients.Clear();
            AvailableClients.Add("All");
            foreach (var client in clients)
            {
                AvailableClients.Add(client);
            }

            var restoredSelection = AvailableClients.FirstOrDefault(x =>
                string.Equals(x, selectedBeforeRefresh, StringComparison.OrdinalIgnoreCase));
            SelectedClient = restoredSelection ?? "All";
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var row = ResolvePrimarySelectedRow();
        if (!CanDeleteSelectedRow || row is null)
            return;

        if (!_appState.CanArchive)
        {
            _appState.SetStatus("Not permitted.");
            return;
        }

        var ok = await DialogService.Confirm(
            _window,
            "Delete Billing Entry",
            $"Delete this billing entry permanently?\n\n{row.Company}\n{row.Registration}\n\nThis action cannot be undone.");

        if (!ok)
            return;

        using var db = new AppDbContext();
        var entry = await db.BillingEntries.FirstOrDefaultAsync(x => x.Id == row.Id);
        if (entry is null)
        {
            _appState.SetStatus("Billing entry no longer exists.");
            await Load();
            return;
        }

        db.BillingEntries.Remove(entry);
        await db.SaveChangesAsync();

        new AuditService().Log(
            _appState.OperatorName,
            "BILLING_DELETE",
            "BillingEntry",
            entry.Id,
            entry.Registration,
            "Deleted from Billing List");

        _appState.SetStatus($"Billing entry deleted: {entry.Company} / {entry.Registration}.");
        await Load();
    }

    [RelayCommand]
    private async Task ToggleClientLiveTracking(BillingListRow? row)
    {
        if (row is null || !row.IsClientSummaryRow)
            return;

        var companyName = NormalizeClientDisplayName(row.Company);
        if (string.IsNullOrWhiteSpace(companyName))
            return;

        var currentlyEnabled = row.LiveTrackingEnabled ?? true;
        var targetEnabled = !currentlyEnabled;
        var actionText = targetEnabled ? "Enable" : "Disable";

        var ok = await DialogService.Confirm(
            _window,
            "Live Tracking",
            $"{actionText} live tracking for {row.Company}?\n\nThis updates the client TOTAL row.");

        if (!ok)
            return;

        using var db = new AppDbContext();
        var existingRows = await db.ClientQuoteSummaries
            .Where(x => !string.IsNullOrWhiteSpace(x.Company))
            .ToListAsync();

        var matchingRows = existingRows
            .Where(x => string.Equals(
                NormalizeClientDisplayName(x.Company),
                companyName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRows.Count == 0)
        {
            db.ClientQuoteSummaries.Add(new ClientQuoteSummary
            {
                Company = row.Company.Trim(),
                HasLiveTracking = targetEnabled,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            foreach (var summary in matchingRows)
            {
                summary.HasLiveTracking = targetEnabled;
                summary.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(summary.Company))
                    summary.Company = row.Company.Trim();
            }
        }

        await db.SaveChangesAsync();

        _appState.SetStatus($"Live tracking {(targetEnabled ? "enabled" : "disabled")} for {row.Company}.");
        await Load();
    }

    private static ClientQuoteSummary BuildPackageSummaryFromEntries(string company, IReadOnlyCollection<BillingEntry> entries)
    {
        var summary = new ClientQuoteSummary
        {
            Company = company,
            HasLiveTracking = entries.Count > 0
        };

        foreach (var entry in entries)
        {
            switch (ResolvePackageFamily(entry))
            {
                case StingPackageFamily.Sting:
                    summary.StingCount++;
                    break;
                case StingPackageFamily.StingPlus:
                    summary.StingPlusCount++;
                    break;
                case StingPackageFamily.StingFm:
                    summary.StingFmCount++;
                    break;
            }
        }

        return summary;
    }

    private static StingPackageFamily ResolvePackageFamily(BillingEntry entry)
    {
        var fromSelectedPackage = StingPackageClassifier.Classify(entry.StingPackageType);
        if (fromSelectedPackage != StingPackageFamily.Unknown)
            return fromSelectedPackage;

        var fromNotes = StingPackageClassifier.Classify(entry.Notes);
        if (fromNotes != StingPackageFamily.Unknown)
            return fromNotes;

        var fromReason = StingPackageClassifier.Classify(entry.Reason);
        if (fromReason != StingPackageFamily.Unknown)
            return fromReason;

        return StingPackageFamily.Unknown;
    }

    private static string ResolvePackageLabel(BillingEntry entry)
    {
        return ResolvePackageFamily(entry) switch
        {
            StingPackageFamily.Sting => "STING",
            StingPackageFamily.StingPlus => "STING PLUS",
            StingPackageFamily.StingFm => "STING FM",
            _ => "-"
        };
    }

    private static string BuildVehicleDescription(BillingEntry entry)
    {
        var make = entry.Make?.Trim();
        var model = entry.Model?.Trim();
        var colour = entry.Colour?.Trim();

        var makeModel = string.Join(" ", new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (string.IsNullOrWhiteSpace(makeModel) && string.IsNullOrWhiteSpace(colour))
            return "";

        if (string.IsNullOrWhiteSpace(makeModel))
            return colour ?? "";

        if (string.IsNullOrWhiteSpace(colour))
            return makeModel;

        return $"{makeModel} - {colour}";
    }

    private static string BuildCode(BillingEntry entry)
    {
        var unit = string.IsNullOrWhiteSpace(entry.TrackingUnitMake) ? "-" : entry.TrackingUnitMake.Trim();
        var serial = !string.IsNullOrWhiteSpace(entry.SerialNumber)
            ? entry.SerialNumber.Trim()
            : (!string.IsNullOrWhiteSpace(entry.Imei) ? entry.Imei.Trim() : "-");
        return $"{unit} - {serial}";
    }

    private BillingListRow? FindNearestDataRow(int preferredIndex)
    {
        if (Rows.Count == 0)
            return null;

        var clampedIndex = Math.Clamp(preferredIndex, 0, Rows.Count - 1);
        for (var offset = 0; offset < Rows.Count; offset++)
        {
            var forward = clampedIndex + offset;
            if (forward < Rows.Count && !Rows[forward].IsClientSummaryRow)
                return Rows[forward];

            var backward = clampedIndex - offset;
            if (backward >= 0 && !Rows[backward].IsClientSummaryRow)
                return Rows[backward];
        }

        return null;
    }

    private static string NormalizeClientDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [RelayCommand]
    private async Task ViewDetails()
    {
        var row = ResolvePrimarySelectedRow();
        if (row is null || row.IsClientSummaryRow || row.Id <= 0) return;

        var dlg = new StingListManager.Views.InstallationDetailsWindow();
        dlg.DataContext = new InstallationDetailsViewModel(() => dlg.Close(), row.Id, _appState);
        await dlg.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        var row = ResolvePrimarySelectedRow();
        if (!CanEditSelectedRow || row is null)
            return;

        var dlg = new StingListManager.Views.BillingEntryEditWindow();
        dlg.DataContext = new BillingEntryEditViewModel(
            row.Id,
            () => dlg.Close(),
            () => _ = Load(),
            _appState);

        await dlg.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task ExportToExcel()
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Billing List Export",
            SuggestedFileName = $"Billing List {DateTime.Now:yyyy-MM-dd}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel file") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file is null) return;

        var path = file.Path.LocalPath;
        var exporter = new ExcelExportService();
        exporter.ExportBillingList(path);

        _appState.SetStatus($"Billing list exported: {Path.GetFileName(path)}");
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedRow));
        OnPropertyChanged(nameof(CanDeleteSelectedRow));
    }

    private BillingListRow? ResolvePrimarySelectedRow()
    {
        if (SelectedRow != null)
            return SelectedRow;

        return SelectedRows?.FirstOrDefault();
    }
}
