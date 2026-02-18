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

public partial class StingListRow : ObservableObject
{
    public int Id { get; set; }
    public int? LocalBillingEntryId { get; set; }
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
    public DateTime ActiveFrom { get; set; }
    public bool HasLocalBillingEntry => LocalBillingEntryId is > 0;
}

public partial class StingListViewModel : PagedViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly List<StingListRow> _allRows = new();
    private WialonApiService? _wialonService;
    private bool _suppressFilterReload;
    private bool _isLoadingFromWialon;

    public ObservableCollection<StingListRow> Rows { get; } = new();
    public ObservableCollection<FilterPreset> Presets { get; } = new();
    public ObservableCollection<string> AvailableCompanies { get; } = new();
    public ObservableCollection<string> AvailableRegistrations { get; } = new();
    public List<string> StatusOptions { get; } = new();

    [ObservableProperty] private StingListRow? selectedRow;
    [ObservableProperty] private bool showArchived;
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private FilterPreset? selectedPreset;
    [ObservableProperty] private string selectedStatus = "Any";
    [ObservableProperty] private string? companyFilter;
    [ObservableProperty] private string? registrationFilter;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;

    public StingListViewModel(Window window, AppState appState, DateTime? startDate = null, DateTime? endDate = null, string? statusFilter = null)
    {
        _window = window;
        _appState = appState;

        StatusOptions.AddRange(new[]
        {
            "Any",
            "Current",
            BillingStatus.Active.ToString(),
            "Not Loaded",
            BillingStatus.Removed.ToString()
        });

        SelectedStatus = string.IsNullOrWhiteSpace(statusFilter) ? "Any" : statusFilter;
        SetDefaultDateRange(startDate, endDate);

        Presets.Clear();
        foreach (var p in _appState.Settings.StingPresets)
            Presets.Add(p);

        LoadPage();
        _ = ReloadFromWialonAsync();
    }

    public bool CanArchive => _appState.CanArchive;

    public bool CanStartRemoval =>
        SelectedRow != null
        && !string.Equals(SelectedRow.Status, BillingStatus.Removed.ToString(), StringComparison.OrdinalIgnoreCase)
        && !SelectedRow.IsArchived;

    public bool CanModifySelectedRow => CanArchive && SelectedRow?.HasLocalBillingEntry == true;

    partial void OnShowArchivedChanged(bool value) => FirstPageCommand.Execute(null);
    partial void OnSearchTextChanged(string? value) => FirstPageCommand.Execute(null);

    partial void OnSelectedRowChanged(StingListRow? value)
    {
        OnPropertyChanged(nameof(CanStartRemoval));
        OnPropertyChanged(nameof(CanModifySelectedRow));
    }

    partial void OnSelectedStatusChanged(string value) => FirstPageCommand.Execute(null);

    partial void OnCompanyFilterChanged(string? value)
    {
        if (_suppressFilterReload) return;
        FirstPageCommand.Execute(null);
    }

    partial void OnRegistrationFilterChanged(string? value)
    {
        if (_suppressFilterReload) return;
        FirstPageCommand.Execute(null);
    }

    partial void OnStartDateChanged(DateTimeOffset? value) => FirstPageCommand.Execute(null);
    partial void OnEndDateChanged(DateTimeOffset? value) => FirstPageCommand.Execute(null);

    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        if (value is null) return;
        ShowArchived = value.ShowArchived;
        SearchText = value.CompanyContains;
    }

    [RelayCommand]
    private async Task Refresh() => await ReloadFromWialonAsync();

    protected override void LoadPage()
    {
        var selectedKey = (SelectedRow?.Id, SelectedRow?.LocalBillingEntryId);

        var filteredRows = BuildFilteredRows(applyPaging: false);
        var pageRows = filteredRows.Skip(Skip).Take(PageSize).ToList();

        Rows.Clear();
        foreach (var row in pageRows)
        {
            Rows.Add(row);
        }

        if (selectedKey.Id is not null)
        {
            SelectedRow = Rows.FirstOrDefault(r =>
                r.Id == selectedKey.Id &&
                r.LocalBillingEntryId == selectedKey.LocalBillingEntryId);
        }

        _appState.SetStatus($"Loaded STING entries from Wialon: page {PageNumber} ({Rows.Count} of {filteredRows.Count})");
        OnPropertyChanged(nameof(CanStartRemoval));
        OnPropertyChanged(nameof(CanModifySelectedRow));
    }

    private List<StingListRow> BuildFilteredRows(bool applyPaging)
    {
        IEnumerable<StingListRow> query = _allRows;

        if (!ShowArchived)
            query = query.Where(r => !r.IsArchived);

        if (!string.Equals(SelectedStatus, "Any", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(SelectedStatus, "Current", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r =>
                    string.Equals(r.Status, BillingStatus.Active.ToString(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Status, "Not Loaded", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(SelectedStatus, "Not Loaded", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => string.Equals(r.Status, "Not Loaded", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                query = query.Where(r => string.Equals(r.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(r =>
                ContainsIgnoreCase(r.Company, search)
                || ContainsIgnoreCase(r.Registration, search)
                || ContainsIgnoreCase(r.FleetNumber, search)
                || ContainsIgnoreCase(r.Make, search)
                || ContainsIgnoreCase(r.Model, search)
                || ContainsIgnoreCase(r.Imei, search)
                || ContainsIgnoreCase(r.SerialNumber, search));
        }

        if (StartDate is not null)
        {
            var start = StartDate.Value.Date;
            query = query.Where(r => r.ActiveFrom >= start);
        }

        if (EndDate is not null)
        {
            var endExclusive = EndDate.Value.Date.AddDays(1);
            query = query.Where(r => r.ActiveFrom < endExclusive);
        }

        var baseFiltered = query.ToList();
        RefreshFilterOptions(baseFiltered);

        IEnumerable<StingListRow> finalQuery = baseFiltered;

        if (!string.IsNullOrWhiteSpace(CompanyFilter))
        {
            var company = CompanyFilter.Trim();
            finalQuery = finalQuery.Where(r => string.Equals(r.Company, company, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(RegistrationFilter))
        {
            var registration = RegistrationFilter.Trim();
            finalQuery = finalQuery.Where(r => string.Equals(r.Registration, registration, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = finalQuery
            .OrderByDescending(r => r.ActiveFrom)
            .ThenBy(r => r.Company)
            .ThenBy(r => r.Registration)
            .ToList();

        if (!applyPaging)
            return ordered;

        return ordered.Skip(Skip).Take(PageSize).ToList();
    }

    private void RefreshFilterOptions(IReadOnlyCollection<StingListRow> rows)
    {
        var companies = rows
            .Select(x => x.Company)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        IEnumerable<StingListRow> registrationSource = rows;
        if (!string.IsNullOrWhiteSpace(CompanyFilter))
        {
            var selectedCompany = CompanyFilter.Trim();
            registrationSource = registrationSource.Where(x =>
                string.Equals(x.Company, selectedCompany, StringComparison.OrdinalIgnoreCase));
        }

        var registrations = registrationSource
            .Select(x => x.Registration)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        ReplaceOptions(AvailableCompanies, companies);
        ReplaceOptions(AvailableRegistrations, registrations);

        _suppressFilterReload = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(CompanyFilter) &&
                !companies.Any(x => string.Equals(x, CompanyFilter, StringComparison.OrdinalIgnoreCase)))
            {
                CompanyFilter = null;
            }

            if (!string.IsNullOrWhiteSpace(RegistrationFilter) &&
                !registrations.Any(x => string.Equals(x, RegistrationFilter, StringComparison.OrdinalIgnoreCase)))
            {
                RegistrationFilter = null;
            }
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IReadOnlyCollection<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private async Task ReloadFromWialonAsync()
    {
        if (_isLoadingFromWialon)
            return;

        var token = _appState.Settings.WialonApiToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            _allRows.Clear();
            FirstPageCommand.Execute(null);
            _appState.SetStatus("STING list now loads from Wialon. Add your Wialon token in Settings.");
            return;
        }

        _isLoadingFromWialon = true;
        try
        {
            _appState.SetStatus("Loading STING list from Wialon...");

            _wialonService ??= new WialonApiService(token);
            var connected = await _wialonService.TestConnectionAsync();
            if (!connected)
            {
                var err = string.IsNullOrWhiteSpace(_wialonService.LastError)
                    ? "Unknown error"
                    : _wialonService.LastError;

                _allRows.Clear();
                FirstPageCommand.Execute(null);
                _appState.SetStatus($"Failed to connect to Wialon for STING list: {err}");
                return;
            }

            var reports = await LoadAllReportsAsync(_wialonService);
            MapReportsToRows(reports);

            PageNumber = 1;
            LoadPage();
            _appState.SetStatus($"Loaded {_allRows.Count} STING entries from Wialon.");
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Failed to load STING list from Wialon: {ex.Message}");
        }
        finally
        {
            _isLoadingFromWialon = false;
        }
    }

    private void MapReportsToRows(IReadOnlyCollection<WialonReport> reports)
    {
        using var db = new AppDbContext();
        var billingEntries = db.BillingEntries
            .AsNoTracking()
            .OrderByDescending(x => x.ActiveFrom)
            .ToList();

        var localByImei = billingEntries
            .Select(x => new { Key = NormalizeDigits(x.Imei), Entry = x })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Entry, StringComparer.Ordinal);

        var localByCompanyReg = billingEntries
            .Select(x => new { Key = BuildCompanyRegistrationKey(x.Company, x.Registration), Entry = x })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Entry, StringComparer.Ordinal);

        _allRows.Clear();

        var fallbackId = 1;
        foreach (var report in reports)
        {
            var localMatch = FindLocalMatch(report, localByImei, localByCompanyReg);
            var activeFrom = localMatch?.ActiveFrom ?? (report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt);
            var status = !string.IsNullOrWhiteSpace(report.Status)
                ? report.Status
                : BillingStatus.Active.ToDisplayString();

            _allRows.Add(new StingListRow
            {
                Id = report.Id > 0 ? report.Id : -fallbackId++,
                LocalBillingEntryId = localMatch?.Id,
                Company = FirstNonEmpty(report.Client, localMatch?.Company) ?? "Unknown",
                Registration = FirstNonEmpty(report.Registration, localMatch?.Registration, report.Name) ?? string.Empty,
                FleetNumber = FirstNonEmpty(report.FleetNumber, localMatch?.FleetNumber),
                Make = FirstNonEmpty(report.Make, localMatch?.Make),
                Model = FirstNonEmpty(report.Model, localMatch?.Model),
                Colour = FirstNonEmpty(report.Colour, localMatch?.Colour),
                VinNumber = FirstNonEmpty(report.VinNumber, localMatch?.VinNumber),
                TrackingUnitMake = FirstNonEmpty(report.TrackingUnitMake, report.UnitType, localMatch?.TrackingUnitMake),
                Imei = FirstNonEmpty(report.Imei, report.UniqueId, localMatch?.Imei),
                SerialNumber = FirstNonEmpty(report.SerialNumber, localMatch?.SerialNumber),
                Iccid = FirstNonEmpty(report.Iccid, localMatch?.Iccid),
                SimNumber = localMatch?.SimNumber,
                Notes = FirstNonEmpty(report.Notes, localMatch?.Notes),
                Status = status,
                IsArchived = localMatch?.ArchivedAt != null,
                ActiveFrom = activeFrom
            });
        }
    }

    private static BillingEntry? FindLocalMatch(
        WialonReport report,
        IReadOnlyDictionary<string, BillingEntry> localByImei,
        IReadOnlyDictionary<string, BillingEntry> localByCompanyReg)
    {
        var imeiKey = NormalizeDigits(FirstNonEmpty(report.Imei, report.UniqueId));
        if (!string.IsNullOrWhiteSpace(imeiKey) && localByImei.TryGetValue(imeiKey, out var byImei))
            return byImei;

        var registration = FirstNonEmpty(report.Registration, report.Name);
        var companyRegKey = BuildCompanyRegistrationKey(report.Client, registration);
        if (!string.IsNullOrWhiteSpace(companyRegKey) && localByCompanyReg.TryGetValue(companyRegKey, out var byCompanyReg))
            return byCompanyReg;

        return null;
    }

    private static async Task<List<WialonReport>> LoadAllReportsAsync(WialonApiService service)
    {
        const int batchSize = 200;

        var (firstBatch, totalCount) = await service.GetReportsAsync(0, batchSize, "sys_name", "*");
        var all = new List<WialonReport>(firstBatch);

        for (var from = batchSize; from < totalCount; from += batchSize)
        {
            var (batch, _) = await service.GetReportsAsync(from, batchSize, "sys_name", "*");
            all.AddRange(batch);
        }

        var deduped = new Dictionary<string, WialonReport>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in all)
        {
            var key = report.Id > 0
                ? $"id:{report.Id}"
                : $"uid:{NormalizeLookup(FirstNonEmpty(report.UniqueId, report.Imei, report.Name))}";

            if (!deduped.ContainsKey(key))
                deduped[key] = report;
        }

        return deduped.Values.ToList();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedStatus = "Any";
        CompanyFilter = null;
        RegistrationFilter = null;
        SearchText = null;
        ShowArchived = false;
        SetDefaultDateRange(null, null);
        FirstPageCommand.Execute(null);
    }

    private void SetDefaultDateRange(DateTime? start, DateTime? end)
    {
        if (start != null || end != null)
        {
            StartDate = start != null ? new DateTimeOffset(start.Value.Date) : null;
            EndDate = end != null ? new DateTimeOffset(end.Value.Date) : null;
            return;
        }

        StartDate = null;
        EndDate = null;
    }

    [RelayCommand]
    private async Task MarkRemoved()
    {
        if (!CanArchive)
        {
            _appState.SetStatus("Not permitted.");
            return;
        }

        if (SelectedRow is null)
            return;

        if (SelectedRow.LocalBillingEntryId is null)
        {
            _appState.SetStatus("This Wialon entry is not linked to a local billing record.");
            return;
        }

        var ok = await DialogService.Confirm(
            _window,
            "Mark Removed",
            $"Mark this unit as REMOVED?\n\n{SelectedRow.Registration}\n\nThis will stop billing and set a removal date.");

        if (!ok) return;

        using var db = new AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(x => x.Id == SelectedRow.LocalBillingEntryId.Value);
        if (entry is null)
        {
            _appState.SetStatus("Linked billing entry was not found.");
            return;
        }

        entry.Status = BillingStatus.Removed;
        entry.ActiveTo = DateTime.UtcNow;

        db.SaveChanges();
        _appState.SetStatus("Unit marked as removed.");
        await ReloadFromWialonAsync();
    }

    [RelayCommand]
    private async Task Archive()
    {
        if (!CanArchive)
        {
            _appState.SetStatus("Not permitted.");
            return;
        }

        if (SelectedRow is null)
            return;

        if (SelectedRow.LocalBillingEntryId is null)
        {
            _appState.SetStatus("This Wialon entry is not linked to a local billing record.");
            return;
        }

        var ok = await DialogService.Confirm(
            _window,
            "Archive Entry",
            $"Archive this entry?\n\n{SelectedRow.Registration}\n\nIt will be hidden from the active billing list.");

        if (!ok) return;

        using var db = new AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(x => x.Id == SelectedRow.LocalBillingEntryId.Value);
        if (entry is null)
        {
            _appState.SetStatus("Linked billing entry was not found.");
            return;
        }

        entry.ArchivedAt = DateTime.UtcNow;

        db.SaveChanges();
        _appState.SetStatus("Entry archived.");
        await ReloadFromWialonAsync();
    }

    [RelayCommand]
    private void StartRemoval()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "sting_debug.log");
        File.AppendAllText(logPath, "[StartRemoval] METHOD CALLED" + Environment.NewLine);

        if (SelectedRow is null)
        {
            File.AppendAllText(logPath, "[StartRemoval] SelectedRow is NULL - returning" + Environment.NewLine);
            _appState.SetStatus("No entry selected.");
            return;
        }

        if (SelectedRow.Status == BillingStatus.Removed.ToString() || SelectedRow.IsArchived)
        {
            const string msg = "Cannot create removal request: This entry is already marked as removed or archived.";
            File.AppendAllText(logPath, "[StartRemoval] " + msg + Environment.NewLine);
            _appState.SetStatus(msg);
            return;
        }

        var logMsg =
            $"[StartRemoval] SelectedRow data: Company={SelectedRow.Company}, Reg={SelectedRow.Registration}, Make={SelectedRow.Make}, Model={SelectedRow.Model}, Imei={SelectedRow.Imei}, SerialNumber={SelectedRow.SerialNumber}, Iccid={SelectedRow.Iccid}";
        File.AppendAllText(logPath, logMsg + Environment.NewLine);

        using var db = new AppDbContext();

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

        logMsg =
            $"[StartRemoval] Quote created with: Make={quote.Make}, Model={quote.Model}, Imei={quote.Imei}, Iccid={quote.Iccid}, SerialNumber={quote.SerialNumber}";
        File.AppendAllText(logPath, logMsg + Environment.NewLine);

        db.Quotes.Add(quote);
        var changes = db.SaveChanges();

        logMsg = $"[StartRemoval] SaveChanges returned {changes}. Quote ID={quote.Id}";
        File.AppendAllText(logPath, logMsg + Environment.NewLine);

        logMsg = $"[StartRemoval] After save: Make={quote.Make}, Model={quote.Model}, Imei={quote.Imei}, Iccid={quote.Iccid}";
        File.AppendAllText(logPath, logMsg + Environment.NewLine);

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
        File.AppendAllText(logPath, logMsg + Environment.NewLine);

        _appState.SetStatus("Removal quote created with linked cancellation request. Navigate to Quotes to approve.");
    }

    [RelayCommand]
    private async Task ViewDetails()
    {
        if (SelectedRow is null)
            return;

        if (SelectedRow.LocalBillingEntryId is null)
        {
            _appState.SetStatus("No local installation details exist for this Wialon-only entry.");
            return;
        }

        var dlg = new StingListManager.Views.InstallationDetailsWindow();
        dlg.DataContext = new InstallationDetailsViewModel(() => dlg.Close(), SelectedRow.LocalBillingEntryId.Value, _appState);
        await dlg.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task ExportToExcel()
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save STING List Export",
            SuggestedFileName = $"STING List {DateTime.Now:yyyy-MM-dd}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel file") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file is null) return;

        var exportRows = BuildFilteredRows(applyPaging: false)
            .Select(x => new StingListExportRow
            {
                Company = x.Company,
                Registration = x.Registration,
                FleetNumber = x.FleetNumber,
                Make = x.Make,
                Model = x.Model,
                Colour = x.Colour,
                VinNumber = x.VinNumber,
                Imei = x.Imei,
                SerialNumber = x.SerialNumber,
                Iccid = x.Iccid,
                Notes = x.Notes,
                Status = x.Status,
                ActiveFrom = x.ActiveFrom
            })
            .ToList();

        var path = file.Path.LocalPath;
        var exporter = new ExcelExportService();
        exporter.ExportStingList(path, exportRows);

        _appState.SetStatus($"STING list exported: {Path.GetFileName(path)}");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static bool ContainsIgnoreCase(string? source, string value)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string BuildCompanyRegistrationKey(string? company, string? registration)
    {
        var companyPart = NormalizeLookup(company);
        var registrationPart = NormalizeLookup(registration);

        if (string.IsNullOrWhiteSpace(companyPart) || string.IsNullOrWhiteSpace(registrationPart))
            return string.Empty;

        return $"{companyPart}|{registrationPart}";
    }

    private static string NormalizeLookup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }
}
