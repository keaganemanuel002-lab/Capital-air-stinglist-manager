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
    [ObservableProperty] private string selectedClient = "All";
    [ObservableProperty] private string? registrationSearch;

    public bool CanEditSelectedRow =>
        SelectedRow is { IsClientSummaryRow: false, Id: > 0 };

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
        OnPropertyChanged(nameof(CanEditSelectedRow));
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
            filtered = filtered.Where(e =>
                string.Equals(e.Company, SelectedClient, StringComparison.OrdinalIgnoreCase));
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
            var liveTrackingUnits = totalUnits;
            var packageTotal = packageSummary.StingCount + packageSummary.StingPlusCount + packageSummary.StingFmCount;
            var packageSourceNote = packageTotal <= 0
                ? "Package mix unavailable (set package type to STING/STING PLUS/STING FM)"
                : "Package mix from active STING list entries";

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
                Notes = packageSourceNote,
                Reason = "",
                IsClientSummaryRow = true,
                RowBackground = ClientSummaryRowBackground,
                RowForeground = ClientSummaryRowForeground,
                RowFontWeight = FontWeight.Bold
            });
        }

        _appState.SetStatus($"Loaded billing list: {entries.Count} entries across {grouped.Count()} clients ({Rows.Count} rows incl. totals).");
        OnPropertyChanged(nameof(CanEditSelectedRow));
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
        var clients = entries
            .Select(e => e.Company)
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

            if (string.IsNullOrWhiteSpace(SelectedClient)
                || !AvailableClients.Any(x => string.Equals(x, SelectedClient, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedClient = "All";
            }
        }
        finally
        {
            _suppressFilterReload = false;
        }
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

    [RelayCommand]
    private async Task ViewDetails()
    {
        if (SelectedRow is null || SelectedRow.IsClientSummaryRow || SelectedRow.Id <= 0) return;

        var dlg = new StingListManager.Views.InstallationDetailsWindow();
        dlg.DataContext = new InstallationDetailsViewModel(() => dlg.Close(), SelectedRow.Id, _appState);
        await dlg.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (!CanEditSelectedRow || SelectedRow is null)
            return;

        var dlg = new StingListManager.Views.BillingEntryEditWindow();
        dlg.DataContext = new BillingEntryEditViewModel(
            SelectedRow.Id,
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
}
