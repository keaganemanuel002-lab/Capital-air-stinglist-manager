using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
}

public partial class BillingListViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    public ObservableCollection<BillingListRow> Rows { get; } = new();

    [ObservableProperty] private BillingListRow? selectedRow;

    public BillingListViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();

        var entries = db.BillingEntries
            .AsNoTracking()
            .Where(e => e.ArchivedAt == null && (e.Status == BillingStatus.Active || e.Status == BillingStatus.NotLoaded))
            .OrderBy(e => e.Company)
            .ThenBy(e => e.Registration)
            .ToList();

        Rows.Clear();

        var grouped = entries.GroupBy(e => e.Company).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var rowNumber = 1;

            foreach (var entry in group)
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
                    Reason = entry.Reason ?? ""
                });

                rowNumber++;
            }
        }

        _appState.SetStatus($"Loaded billing list: {Rows.Count} entries.");
    }

    private static Dictionary<string, ClientQuoteSummary> BuildQuoteSummaries(List<Quote> quotes)
    {
        var summaries = new Dictionary<string, ClientQuoteSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var quote in quotes)
        {
            if (string.IsNullOrWhiteSpace(quote.Company))
                continue;

            if (!summaries.TryGetValue(quote.Company, out var summary))
            {
                summary = new ClientQuoteSummary { Company = quote.Company };
                summaries[quote.Company] = summary;
            }

            if (quote.IncludesAppLiveTracking || quote.LineItems.Any(li => li.IncludesAppLiveTracking))
            {
                summary.HasLiveTracking = true;
            }

            if (quote.LineItems.Count > 0)
            {
                foreach (var item in quote.LineItems)
                {
                    var qty = item.Quantity <= 0 ? 1 : item.Quantity;
                    ApplyProductType(summary, item.ProductType, qty);
                }
            }
            else
            {
                ApplyProductType(summary, quote.ProductType, 1);
            }
        }

        return summaries;
    }

    private static void ApplyProductType(ClientQuoteSummary summary, string? productType, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productType))
            return;

        var type = productType.Trim();

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

    private static int CountClientEntries(List<BillingEntry> entries, string company)
    {
        return entries.Count(e => string.Equals(e.Company, company, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpsertSummaries(AppDbContext db, Dictionary<string, ClientQuoteSummary> summaries, HashSet<string> activeCompanies)
    {
        // Delete old summaries for active companies to start fresh
        var oldEntries = db.ClientQuoteSummaries.Where(x => activeCompanies.Contains(x.Company)).ToList();
        foreach (var entry in oldEntries)
        {
            db.ClientQuoteSummaries.Remove(entry);
        }

        // Add new summaries
        foreach (var item in summaries.Values)
        {
            db.ClientQuoteSummaries.Add(new ClientQuoteSummary
            {
                Company = item.Company,
                StingCount = item.StingCount,
                StingPlusCount = item.StingPlusCount,
                StingFmCount = item.StingFmCount,
                HasLiveTracking = item.HasLiveTracking,
                UpdatedAt = DateTime.UtcNow
            });
        }

        db.SaveChanges();
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
        if (SelectedRow is null) return;

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
