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
    private static readonly IBrush ClientSummaryRowBackground = new SolidColorBrush(Color.Parse("#EAF0FB"));
    private static readonly IBrush ClientSummaryRowForeground = new SolidColorBrush(Color.Parse("#1E3A8A"));
    private bool _suppressFilterReload;

    public ObservableCollection<BillingListRow> Rows { get; } = new();
    public ObservableCollection<string> AvailableClients { get; } = new();

    [ObservableProperty] private BillingListRow? selectedRow;
    [ObservableProperty] private string selectedClient = "All";
    [ObservableProperty] private string? registrationSearch;

    public BillingListViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        Load();
    }

    partial void OnSelectedClientChanged(string value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    partial void OnRegistrationSearchChanged(string? value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();

        var allEntries = db.BillingEntries
            .AsNoTracking()
            .Where(e => e.ArchivedAt == null && (e.Status == BillingStatus.Active || e.Status == BillingStatus.NotLoaded))
            .OrderBy(e => e.Company)
            .ThenBy(e => e.Registration)
            .ToList();

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

        var activeCompanies = entries
            .Select(e => e.Company)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var approvedQuotes = db.Quotes
            .AsNoTracking()
            .Include(q => q.LineItems)
            .Where(q => q.Status == QuoteStatus.Approved)
            .ToList()
            .Where(q => !string.IsNullOrWhiteSpace(q.Company) && activeCompanies.Contains(q.Company))
            .ToList();

        var quoteSummaries = BuildQuoteSummaries(approvedQuotes);

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

            quoteSummaries.TryGetValue(group.Key, out var quoteSummary);
            quoteSummary ??= new ClientQuoteSummary { Company = group.Key };

            var totalUnits = clientEntries.Count;
            var liveTrackingUnits = quoteSummary.HasLiveTracking ? totalUnits : 0;
            var packageTotal = quoteSummary.StingCount + quoteSummary.StingPlusCount + quoteSummary.StingFmCount;
            var packageSourceNote = packageTotal <= 0
                ? "Package mix unavailable"
                : "Package mix from approved install quotes";

            Rows.Add(new BillingListRow
            {
                Id = 0,
                RowLabel = "TOTAL",
                Company = group.Key,
                Registration = $"{totalUnits} units",
                FleetNumber = $"{liveTrackingUnits} live",
                VehicleDescription =
                    $"STING {quoteSummary.StingCount} | STING PLUS {quoteSummary.StingPlusCount} | STING FM {quoteSummary.StingFmCount}",
                Code = quoteSummary.HasLiveTracking
                    ? "Live tracking: Yes (all client units)"
                    : "Live tracking: No",
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
    }

    [RelayCommand]
    private void ClearFilters()
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

        Load();
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

    private static Dictionary<string, ClientQuoteSummary> BuildQuoteSummaries(List<Quote> quotes)
    {
        var summaries = new Dictionary<string, ClientQuoteSummary>(StringComparer.OrdinalIgnoreCase);

        var orderedQuotes = quotes
            .OrderBy(q => q.ApprovedAt ?? q.CreatedAt)
            .ThenBy(q => q.Id)
            .ToList();

        foreach (var quote in orderedQuotes)
        {
            if (string.IsNullOrWhiteSpace(quote.Company))
                continue;

            if (!summaries.TryGetValue(quote.Company, out var summary))
            {
                summary = new ClientQuoteSummary { Company = quote.Company };
                summaries[quote.Company] = summary;
            }

            if (QuoteHasLiveTracking(quote))
            {
                if (quote.Type == QuoteType.Removal && IsLiveTrackingOnlyQuote(quote))
                    summary.HasLiveTracking = false;
                else if (quote.Type == QuoteType.Install)
                    summary.HasLiveTracking = true;
            }

            if (quote.Type != QuoteType.Install)
                continue;

            if (quote.LineItems.Count > 0)
            {
                foreach (var item in quote.LineItems)
                {
                    if (!IsPackageUnitLineItem(item))
                        continue;

                    var qty = item.Quantity <= 0 ? 1 : item.Quantity;
                    ApplyProductType(summary, item.ProductType, qty);
                }
            }
            else if (IsPackageUnitProductType(quote.ProductType))
            {
                ApplyProductType(summary, quote.ProductType, 1);
            }
        }

        return summaries;
    }

    private static void ApplyProductType(ClientQuoteSummary summary, string? productType, int quantity)
    {
        if (!IsPackageUnitProductType(productType))
            return;

        var type = productType!.Trim();

        if (type.IndexOf("STING FM", StringComparison.OrdinalIgnoreCase) >= 0
            || type.IndexOf("FM", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            summary.StingFmCount += quantity;
        }
        else if (type.IndexOf("STING PLUS", StringComparison.OrdinalIgnoreCase) >= 0
                 || type.IndexOf("PLUS", StringComparison.OrdinalIgnoreCase) >= 0
                 || type.IndexOf("STING+", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            summary.StingPlusCount += quantity;
        }
        else if (type.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            summary.StingCount += quantity;
        }
    }

    private static bool QuoteHasLiveTracking(Quote quote)
    {
        if (quote.IncludesAppLiveTracking)
            return true;

        if (quote.LineItems.Count <= 0)
        {
            return (quote.ProductType?.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        return quote.LineItems.Any(item =>
            item.IncludesAppLiveTracking
            || (item.ProductCode?.IndexOf("APP-LIVE-TRACKING", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (item.ProductName?.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (item.ProductType?.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
    }

    private static bool IsLiveTrackingOnlyQuote(Quote quote)
    {
        if (!QuoteHasLiveTracking(quote))
            return false;

        if (quote.LineItems.Count <= 0)
            return !IsPackageUnitProductType(quote.ProductType);

        return !quote.LineItems.Any(IsPackageUnitLineItem);
    }

    private static bool IsPackageUnitLineItem(QuoteLineItem item)
    {
        if ((item.ProductCode ?? string.Empty).StartsWith("AUTO-MONTHLY-", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(item.ProductCode, "APP-LIVE-TRACKING", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(item.ProductCode, "PANIC-BUTTON", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsPackageUnitProductType(item.ProductType)
               || IsPackageUnitProductType(item.ProductName)
               || IsPackageUnitProductType(item.ProductCode);
    }

    private static bool IsPackageUnitProductType(string? productType)
    {
        if (string.IsNullOrWhiteSpace(productType))
            return false;

        var normalized = productType.Trim().ToUpperInvariant();
        if (!normalized.Contains("STING", StringComparison.Ordinal))
            return false;

        if (normalized.Contains("MONTHLY", StringComparison.Ordinal))
            return false;

        if (normalized.Contains("LIVE TRACKING", StringComparison.Ordinal))
            return false;

        return true;
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
