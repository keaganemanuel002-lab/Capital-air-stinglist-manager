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
    public string InstallationJobCard { get; set; } = "";
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
    public string Warranty { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTime ActiveFrom { get; set; }
    public bool HasLocalBillingEntry => LocalBillingEntryId is > 0;
}

public partial class StingListViewModel : PagedViewModelBase
{
    private const string InactiveStatus = "Inactive";
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly List<StingListRow> _allRows = new();
    private WialonApiService? _wialonService;
    private string? _wialonTokenInUse;
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
            InactiveStatus,
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

    public bool CanStartTransfer =>
        SelectedRow != null
        && SelectedRow.HasLocalBillingEntry
        && !string.Equals(SelectedRow.Status, BillingStatus.Removed.ToString(), StringComparison.OrdinalIgnoreCase)
        && !SelectedRow.IsArchived;

    public bool CanModifySelectedRow => CanArchive && SelectedRow?.HasLocalBillingEntry == true;
    public bool CanEditSelectedRow => CanModifySelectedRow;
    public bool CanAddEntry => CanArchive;

    partial void OnShowArchivedChanged(bool value) => FirstPageCommand.Execute(null);
    partial void OnSearchTextChanged(string? value) => FirstPageCommand.Execute(null);

    partial void OnSelectedRowChanged(StingListRow? value)
    {
        OnPropertyChanged(nameof(CanStartRemoval));
        OnPropertyChanged(nameof(CanStartTransfer));
        OnPropertyChanged(nameof(CanModifySelectedRow));
        OnPropertyChanged(nameof(CanEditSelectedRow));
        OnPropertyChanged(nameof(CanAddEntry));
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

        _appState.SetStatus($"Loaded STING entries: page {PageNumber} ({Rows.Count} of {filteredRows.Count})");
        OnPropertyChanged(nameof(CanStartRemoval));
        OnPropertyChanged(nameof(CanStartTransfer));
        OnPropertyChanged(nameof(CanModifySelectedRow));
        OnPropertyChanged(nameof(CanEditSelectedRow));
        OnPropertyChanged(nameof(CanAddEntry));
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
                    || IsInactiveStatus(r.Status));
            }
            else if (string.Equals(SelectedStatus, InactiveStatus, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => IsInactiveStatus(r.Status));
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
                || ContainsIgnoreCase(r.InstallationJobCard, search)
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

        var localCount = LoadRowsFromLocalBillingEntries();
        var token = _appState.Settings.WialonApiToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            if (_wialonService is not null)
            {
                try
                {
                    await _wialonService.LogoutAndDisposeAsync();
                }
                catch
                {
                    // Best effort cleanup only.
                }

                _wialonService = null;
            }

            _wialonTokenInUse = null;
            _appState.SetStatus(localCount > 0
                ? $"Loaded {localCount} STING entries from local data. Wialon IMEI status check is disabled (no token)."
                : "No STING entries found in local data.");
            return;
        }

        _isLoadingFromWialon = true;
        try
        {
            _appState.SetStatus($"Loaded {localCount} STING entries from local data. Verifying IMEIs in Wialon...");

            var normalizedToken = token.Trim();
            if (_wialonService is null || !string.Equals(_wialonTokenInUse, normalizedToken, StringComparison.Ordinal))
            {
                if (_wialonService is not null)
                {
                    try
                    {
                        await _wialonService.LogoutAndDisposeAsync();
                    }
                    catch
                    {
                        // Best effort cleanup only.
                    }
                }

                _wialonService = new WialonApiService(normalizedToken);
                _wialonTokenInUse = normalizedToken;
            }

            var connected = await _wialonService.TestConnectionAsync();
            if (!connected)
            {
                var err = string.IsNullOrWhiteSpace(_wialonService.LastError)
                    ? "Unknown error"
                    : _wialonService.LastError;

                _appState.SetStatus(localCount > 0
                    ? $"Loaded {localCount} STING entries from local data. Wialon IMEI status check unavailable ({err})."
                    : $"No STING entries found in local data. Wialon IMEI status check unavailable ({err}).");
                return;
            }

            await ApplyWialonImeiStatusesAsync(_wialonService);

            PageNumber = 1;
            LoadPage();
            var activeCount = _allRows.Count(r => string.Equals(r.Status, BillingStatus.Active.ToString(), StringComparison.OrdinalIgnoreCase));
            var inactiveCount = _allRows.Count(r => IsInactiveStatus(r.Status));
            _appState.SetStatus($"Loaded {_allRows.Count} STING entries. Wialon IMEI check complete: {activeCount} active, {inactiveCount} inactive.");
        }
        catch (Exception ex)
        {
            _appState.SetStatus(localCount > 0
                ? $"Loaded {localCount} STING entries from local data. Wialon IMEI status check unavailable ({ex.Message})."
                : $"No STING entries found in local data. Wialon IMEI status check unavailable ({ex.Message}).");
        }
        finally
        {
            _isLoadingFromWialon = false;
        }
    }

    private async Task ApplyWialonImeiStatusesAsync(WialonApiService service)
    {
        var imeiStatusCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var row in _allRows)
        {
            if (string.Equals(row.Status, BillingStatus.Removed.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var imei = NormalizeDigits(row.Imei);
            if (string.IsNullOrWhiteSpace(imei))
            {
                row.Status = InactiveStatus;
                continue;
            }

            if (!imeiStatusCache.TryGetValue(imei, out var isLoaded))
            {
                isLoaded = await service.IsImeiLoadedAsync(imei);
                imeiStatusCache[imei] = isLoaded;
            }

            row.Status = isLoaded ? BillingStatus.Active.ToString() : InactiveStatus;
        }
    }

    private void MapReportsToRows(IReadOnlyCollection<WialonReport> reports)
    {
        using var db = new AppDbContext();
        var billingEntries = db.BillingEntries
            .AsNoTracking()
            .OrderByDescending(x => x.ActiveFrom)
            .ToList();
        var installationJobCardLookup = BuildInstallationJobCardLookup(db, billingEntries);

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
        var matchedLocalEntryIds = new HashSet<int>();

        var fallbackId = 1;
        foreach (var report in reports)
        {
            var localMatch = FindLocalMatch(report, localByImei, localByCompanyReg);
            if (localMatch is not null)
                matchedLocalEntryIds.Add(localMatch.Id);

            var activeFrom = localMatch?.ActiveFrom ?? (report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt);
            var warrantyDisplay = WarrantyService.GetDisplayText(activeFrom);
            var status = !string.IsNullOrWhiteSpace(report.Status)
                ? report.Status
                : BillingStatus.Active.ToDisplayString();

            _allRows.Add(new StingListRow
            {
                Id = report.Id > 0 ? report.Id : -fallbackId++,
                LocalBillingEntryId = localMatch?.Id,
                InstallationJobCard = localMatch is not null
                    && installationJobCardLookup.TryGetValue(localMatch.Id, out var installationJobCard)
                        ? installationJobCard
                        : string.Empty,
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
                Warranty = warrantyDisplay,
                IsArchived = localMatch?.ArchivedAt != null,
                ActiveFrom = activeFrom
            });
        }

        var unmatchedLocalEntries = billingEntries
            .Where(entry => !matchedLocalEntryIds.Contains(entry.Id))
            .ToList();

        foreach (var entry in unmatchedLocalEntries)
        {
            _allRows.Add(CreateRowFromLocalBillingEntry(
                entry,
                installationJobCardLookup.TryGetValue(entry.Id, out var installationJobCard)
                    ? installationJobCard
                    : null));
        }
    }

    private int LoadRowsFromLocalBillingEntries()
    {
        using var db = new AppDbContext();
        var billingEntries = db.BillingEntries
            .AsNoTracking()
            .OrderByDescending(x => x.ActiveFrom)
            .ToList();
        var installationJobCardLookup = BuildInstallationJobCardLookup(db, billingEntries);

        MapLocalBillingEntriesToRows(billingEntries, installationJobCardLookup);
        PageNumber = 1;
        LoadPage();
        return _allRows.Count;
    }

    private void MapLocalBillingEntriesToRows(
        IReadOnlyCollection<BillingEntry> billingEntries,
        IReadOnlyDictionary<int, string> installationJobCardLookup)
    {
        _allRows.Clear();
        foreach (var entry in billingEntries)
        {
            _allRows.Add(CreateRowFromLocalBillingEntry(
                entry,
                installationJobCardLookup.TryGetValue(entry.Id, out var installationJobCard)
                    ? installationJobCard
                    : null));
        }
    }

    private static StingListRow CreateRowFromLocalBillingEntry(BillingEntry entry, string? installationJobCard = null)
    {
        return new StingListRow
        {
            Id = -Math.Max(1, entry.Id),
            LocalBillingEntryId = entry.Id,
            InstallationJobCard = installationJobCard ?? string.Empty,
            Company = entry.Company,
            Registration = entry.Registration,
            FleetNumber = entry.FleetNumber,
            Make = entry.Make,
            Model = entry.Model,
            Colour = entry.Colour,
            VinNumber = entry.VinNumber,
            TrackingUnitMake = entry.TrackingUnitMake,
            Imei = entry.Imei,
            SerialNumber = entry.SerialNumber,
            Iccid = entry.Iccid,
            SimNumber = entry.SimNumber,
            Notes = entry.Notes,
            Status = entry.Status == BillingStatus.Removed ? BillingStatus.Removed.ToString() : InactiveStatus,
            Warranty = WarrantyService.GetDisplayText(entry.ActiveFrom == default ? DateTime.UtcNow : entry.ActiveFrom),
            IsArchived = entry.ArchivedAt != null,
            ActiveFrom = entry.ActiveFrom == default ? DateTime.UtcNow : entry.ActiveFrom
        };
    }

    private static Dictionary<int, string> BuildInstallationJobCardLookup(
        AppDbContext db,
        IReadOnlyCollection<BillingEntry> billingEntries)
    {
        var lookup = new Dictionary<int, string>();
        if (billingEntries.Count == 0)
            return lookup;

        var installJobs = db.JobCards
            .AsNoTracking()
            .Where(j => j.Type == JobType.Install && j.Status == JobStatus.Completed)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .ToList();

        if (installJobs.Count == 0)
            return lookup;

        var jobsByImei = installJobs
            .Where(j => !string.IsNullOrWhiteSpace(NormalizeDigits(j.Imei)))
            .GroupBy(j => NormalizeDigits(j.Imei), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var jobsBySerial = installJobs
            .Where(j => !string.IsNullOrWhiteSpace(NormalizeLookup(j.SerialNumber)))
            .GroupBy(j => NormalizeLookup(j.SerialNumber), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var jobsByIccid = installJobs
            .Where(j => !string.IsNullOrWhiteSpace(NormalizeDigits(j.Iccid)))
            .GroupBy(j => NormalizeDigits(j.Iccid), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var jobsByCompanyReg = installJobs
            .Where(j => !string.IsNullOrWhiteSpace(BuildCompanyRegistrationKey(j.Company, j.Registration)))
            .GroupBy(j => BuildCompanyRegistrationKey(j.Company, j.Registration), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var entry in billingEntries)
        {
            JobCard? match = null;

            var imeiKey = NormalizeDigits(entry.Imei);
            if (!string.IsNullOrWhiteSpace(imeiKey))
                jobsByImei.TryGetValue(imeiKey, out match);

            if (match is null)
            {
                var serialKey = NormalizeLookup(entry.SerialNumber);
                if (!string.IsNullOrWhiteSpace(serialKey))
                    jobsBySerial.TryGetValue(serialKey, out match);
            }

            if (match is null)
            {
                var iccidKey = NormalizeDigits(entry.Iccid);
                if (!string.IsNullOrWhiteSpace(iccidKey))
                    jobsByIccid.TryGetValue(iccidKey, out match);
            }

            if (match is null)
            {
                var companyRegKey = BuildCompanyRegistrationKey(entry.Company, entry.Registration);
                if (!string.IsNullOrWhiteSpace(companyRegKey))
                    jobsByCompanyReg.TryGetValue(companyRegKey, out match);
            }

            if (match is not null)
                lookup[entry.Id] = JobCardReferenceFormatter.Format(match.Type, match.JobCardNumber);
        }

        return lookup;
    }

    private static bool IsInactiveStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return string.Equals(status, InactiveStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, BillingStatus.NotLoaded.ToDisplayString(), StringComparison.OrdinalIgnoreCase);
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
    private async Task AddEntry()
    {
        if (!CanAddEntry)
        {
            _appState.SetStatus("Not permitted.");
            return;
        }

        var dlg = new StingListManager.Views.BillingEntryEditWindow();
        dlg.DataContext = new BillingEntryEditViewModel(
            () => dlg.Close(),
            () => { },
            _appState);

        await dlg.ShowDialog(_window);
        await ReloadFromWialonAsync();
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (!CanEditSelectedRow || SelectedRow?.LocalBillingEntryId is not > 0)
            return;

        var dlg = new StingListManager.Views.BillingEntryEditWindow();
        dlg.DataContext = new BillingEntryEditViewModel(
            SelectedRow.LocalBillingEntryId.Value,
            () => dlg.Close(),
            () => { },
            _appState);

        await dlg.ShowDialog(_window);
        await ReloadFromWialonAsync();
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
        _appState.SetStatus("STING entry marked as removed.");
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
        _appState.SetStatus("STING entry archived.");
        await ReloadFromWialonAsync();
    }

    [RelayCommand]
    private async Task StartTransfer()
    {
        if (SelectedRow is null)
        {
            _appState.SetStatus("No STING entry selected.");
            return;
        }

        if (SelectedRow.LocalBillingEntryId is null)
        {
            _appState.SetStatus("Cannot create transfer job card: selected row is not linked to a local billing entry.");
            return;
        }

        if (SelectedRow.IsArchived || string.Equals(SelectedRow.Status, BillingStatus.Removed.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _appState.SetStatus("Cannot create transfer job card for removed/archived entries.");
            return;
        }

        using var db = new AppDbContext();
        var sourceEntry = db.BillingEntries.FirstOrDefault(x => x.Id == SelectedRow.LocalBillingEntryId.Value);
        if (sourceEntry is null)
        {
            _appState.SetStatus("The selected billing entry no longer exists.");
            return;
        }

        var nextJobCardNumber = (db.JobCards.Select(x => (int?)x.JobCardNumber).Max() ?? 0) + 1;
        var transferReference = JobCardReferenceFormatter.Format(JobType.Transfer, nextJobCardNumber);

        var transferQuote = new Quote
        {
            QuoteNumber = QuoteNumberAllocator.GetNext(db),
            Type = QuoteType.Install,
            Status = QuoteStatus.Draft,
            Company = sourceEntry.Company,
            Registration = sourceEntry.Registration,
            FleetNumber = sourceEntry.FleetNumber,
            AmountExVat = WorkflowService.TransferInstallFeeExVat,
            Notes = $"Auto-created transfer installation fee quote for {transferReference}. {WorkflowService.TransferFeeOnlyQuoteMarker}"
        };

        transferQuote.LineItems.Add(new QuoteLineItem
        {
            LineNumber = 1,
            ProductType = "Transfer Installation Fee",
            ProductCode = WorkflowService.TransferInstallFeeCode,
            ProductName = "Transfer Installation Fee",
            Quantity = 1,
            UnitPriceExVat = WorkflowService.TransferInstallFeeExVat,
            LineTotalExVat = WorkflowService.TransferInstallFeeExVat,
            IsVatExempt = false,
            Description = "Auto-created transfer installation fee"
        });

        var transferJob = new JobCard
        {
            JobCardNumber = nextJobCardNumber,
            Type = JobType.Transfer,
            Status = JobStatus.Open,
            Company = sourceEntry.Company,
            Registration = sourceEntry.Registration,
            FleetNumber = sourceEntry.FleetNumber,
            Make = sourceEntry.Make,
            Model = sourceEntry.Model,
            Colour = sourceEntry.Colour,
            VinNumber = sourceEntry.VinNumber,
            TrackingUnitMake = sourceEntry.TrackingUnitMake,
            Imei = sourceEntry.Imei,
            SerialNumber = sourceEntry.SerialNumber,
            Iccid = sourceEntry.Iccid,
            SimNumber = sourceEntry.SimNumber,
            Notes = $"Transfer request from {sourceEntry.Company} / {sourceEntry.Registration}"
        };

        db.Quotes.Add(transferQuote);
        db.JobCards.Add(transferJob);
        db.SaveChanges();

        var transferQuoteReference = QuoteReferenceFormatter.Format(transferQuote.QuoteNumber);

        new AuditService().Log(
            _appState.OperatorName,
            "JOB_TRANSFER_CREATE",
            "JobCard",
            transferJob.Id,
            transferJob.Registration,
            $"Transfer job card {transferReference} created from STING list");

        var dlg = new StingListManager.Views.JobCardEditWindow();
        dlg.DataContext = new JobCardEditViewModel(transferJob.Id, () => dlg.Close(), _appState);
        await dlg.ShowDialog(_window);

        _appState.SetStatus(
            $"Transfer job card {transferReference} created. Draft quote {transferQuoteReference} (R{WorkflowService.TransferInstallFeeExVat:0.00} ex VAT) was also created for transfer installation fees.");
    }

    [RelayCommand]
    private async Task StartRemoval()
    {
        if (SelectedRow is null)
        {
            _appState.SetStatus("No entry selected.");
            return;
        }

        if (SelectedRow.Status == BillingStatus.Removed.ToString() || SelectedRow.IsArchived)
        {
            _appState.SetStatus("Cannot create removal request: This entry is already marked as removed or archived.");
            return;
        }

        var isWithinWarranty = WarrantyService.IsWithinWarranty(SelectedRow.ActiveFrom);
        var createRemovalJobCard = true;
        if (!isWithinWarranty)
        {
            createRemovalJobCard = await DialogService.Confirm(
                _window,
                "Out of Warranty Removal",
                "This unit is out of warranty.\n\nCreate a removal job card?\n\nYes = create removal job card when quote is approved\nNo = approve removal quote without creating a job card");
        }

        using var db = new AppDbContext();
        var removalNote = $"Removal for unit: {SelectedRow.Registration}";
        if (!createRemovalJobCard)
            removalNote = $"{removalNote} {WorkflowService.NoRemovalJobCardMarker}";

        var quote = new Quote
        {
            QuoteNumber = QuoteNumberAllocator.GetNext(db),
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
            AmountExVat = _appState.Settings.DefaultRemovalFeeExVat,
            Notes = removalNote
        };

        quote.LineItems.Add(new QuoteLineItem
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

        db.Quotes.Add(quote);
        db.SaveChanges();

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

        var quoteReference = QuoteReferenceFormatter.Format(quote.QuoteNumber);
        var statusMessage = createRemovalJobCard
            ? $"Removal quote {quoteReference} created with linked cancellation request. Approve it in Quotes to generate the removal job card."
            : $"Removal quote {quoteReference} created with linked cancellation request. This out-of-warranty removal is marked to approve without a job card.";
        _appState.SetStatus(statusMessage);
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
                InstallationJobCard = x.InstallationJobCard,
                FleetNumber = x.FleetNumber,
                Make = x.Make,
                Model = x.Model,
                Colour = x.Colour,
                VinNumber = x.VinNumber,
                Imei = x.Imei,
                SerialNumber = x.SerialNumber,
                Iccid = x.Iccid,
                Warranty = x.Warranty,
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
