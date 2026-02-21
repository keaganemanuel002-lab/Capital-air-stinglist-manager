using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class JobCardEditViewModel : ViewModelBase
{
    private readonly int _jobCardId;
    private readonly Action _close;
    private readonly AppState _appState;
    private readonly VehicleDataService _vehicleService = new();
    private bool _suppressMakeFilter;
    private bool _suppressModelFilter;
    private bool _suppressIccidAutoLookup;
    private string? _lastAutoLookupIccid;
    private CancellationTokenSource? _flickswitchLookupCts;
    private JobStatus _currentStatus;

    [ObservableProperty] private string company = "";
    [ObservableProperty] private string registration = "";
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? make;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? colour;
    [ObservableProperty] private string? vinNumber;
    [ObservableProperty] private string? trackingUnitMake;
    [ObservableProperty] private string? imei;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? iccid;
    [ObservableProperty] private string? simNumber;
    [ObservableProperty] private string? flickswitchStatusMessage;
    [ObservableProperty] private string? flickswitchRulesText;
    [ObservableProperty] private decimal? simAirtimeBalance;
    [ObservableProperty] private decimal? simDataBalanceMb;
    [ObservableProperty] private decimal? simSmsBalance;
    [ObservableProperty] private DateTimeOffset? simLastBalanceCheckAt;
    [ObservableProperty] private string? flickswitchBalanceStatusMessage;
    [ObservableProperty] private bool isLookingUpFlickswitch;
    [ObservableProperty] private bool showMakesList;
    [ObservableProperty] private bool showModelsList;
    [ObservableProperty] private bool isEditable = true;
    [ObservableProperty] private string editableWarning = "";
    [ObservableProperty] private bool isTransferCard;
    [ObservableProperty] private string? selectedClientName;

    public ObservableCollection<string> AvailableMakes { get; } = new();
    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> FilteredMakes { get; } = new();
    public ObservableCollection<string> FilteredModels { get; } = new();
    public ObservableCollection<string> ClientNames { get; } = new();
    public bool ShowTransferCompanyPicker => IsTransferCard;
    public bool ShowReadOnlyCompany => !IsTransferCard;
    public double EditSectionOpacity => IsEditable ? 1.0 : 0.95;
    public bool HasFlickswitchRules => !string.IsNullOrWhiteSpace(FlickswitchRulesText);
    public bool HasSimBalanceData =>
        SimAirtimeBalance.HasValue
        || SimDataBalanceMb.HasValue
        || SimSmsBalance.HasValue
        || SimLastBalanceCheckAt.HasValue;
    public string SimAirtimeBalanceDisplay => SimAirtimeBalance.HasValue ? $"R {SimAirtimeBalance.Value:0.00}" : "-";
    public string SimDataBalanceDisplay => SimDataBalanceMb.HasValue ? $"{SimDataBalanceMb.Value:0.##} MB" : "-";
    public string SimSmsBalanceDisplay => SimSmsBalance.HasValue ? $"{SimSmsBalance.Value:0.##}" : "-";
    public string SimLastBalanceCheckDisplay => SimLastBalanceCheckAt.HasValue ? SimLastBalanceCheckAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";

    partial void OnIsTransferCardChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTransferCompanyPicker));
        OnPropertyChanged(nameof(ShowReadOnlyCompany));
    }

    partial void OnIsEditableChanged(bool value)
    {
        OnPropertyChanged(nameof(EditSectionOpacity));
    }

    partial void OnSelectedClientNameChanged(string? value)
    {
        if (!IsTransferCard || string.IsNullOrWhiteSpace(value))
            return;

        Company = value.Trim();
    }

    partial void OnFlickswitchRulesTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFlickswitchRules));
    }

    partial void OnSimAirtimeBalanceChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimDataBalanceMbChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimSmsBalanceChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimLastBalanceCheckAtChanged(DateTimeOffset? value) => OnSimBalanceValuesChanged();

    partial void OnIccidChanged(string? value)
    {
        if (_suppressIccidAutoLookup)
            return;

        _ = AutoLookupFromIccidAsync(value);
    }

    partial void OnMakeChanged(string? value)
    {
        // When make changes, update available models and clear selected model if make is different
        if (!_suppressModelFilter)
        {
            Model = null;
        }
        UpdateAvailableModels();
        FilterModels(Model);
    }

    public void FilterMakes(string? searchText)
    {
        if (_suppressMakeFilter)
        {
            _suppressMakeFilter = false;
            return;
        }

        if (string.Equals(searchText, Make, StringComparison.OrdinalIgnoreCase))
        {
            ShowMakesList = false;
            return;
        }

        FilteredMakes.Clear();
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
        {
            ShowMakesList = false;
            return;
        }
        
        var search = searchText.ToLowerInvariant();
        foreach (var make in AvailableMakes.Where(m => m.ToLowerInvariant().Contains(search)))
        {
            FilteredMakes.Add(make);
        }
        ShowMakesList = FilteredMakes.Count > 0;
    }

    public void FilterModels(string? searchText)
    {
        if (_suppressModelFilter)
        {
            _suppressModelFilter = false;
            return;
        }

        if (string.Equals(searchText, Model, StringComparison.OrdinalIgnoreCase))
        {
            ShowModelsList = false;
            return;
        }

        FilteredModels.Clear();
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
        {
            ShowModelsList = false;
            return;
        }
        
        var search = searchText.ToLowerInvariant();
        foreach (var model in AvailableModels.Where(m => m.ToLowerInvariant().Contains(search)))
        {
            FilteredModels.Add(model);
        }
        ShowModelsList = FilteredModels.Count > 0;
    }

    public void SelectMake(string value)
    {
        _suppressMakeFilter = true;
        _suppressModelFilter = true;
        Make = value;
        Model = null;
        ShowMakesList = false;
    }

    public void SelectModel(string value)
    {
        _suppressModelFilter = true;
        Model = value;
        ShowModelsList = false;
    }

    public JobCardEditViewModel(int jobCardId, Action close, AppState appState)
    {
        _jobCardId = jobCardId;
        _close = close;
        _appState = appState;

        LoadClients();

        // Load all available makes
        RefreshAvailableMakes();

        using var db = new AppDbContext();
        var job = db.JobCards.Find(jobCardId);
        
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
        
        if (job != null)
        {
            _currentStatus = job.Status;
            IsTransferCard = job.Type == JobType.Transfer;
            
            // Completed job cards are locked for editing (including transfer cards).
            if (job.Status == JobStatus.Completed)
            {
                IsEditable = false;
                EditableWarning = "This job card is completed and is read-only.";
            }

            var logMsg = $"[JobCardEditViewModel] Loaded JobCard {jobCardId}: Make={job.Make}, Model={job.Model}, Imei={job.Imei}, Iccid={job.Iccid}, SerialNumber={job.SerialNumber}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            Company = job.Company;
            AddClientNameIfMissing(Company);
            SelectedClientName = Company;
            Registration = job.Registration;
            FleetNumber = job.FleetNumber;
            Make = job.Make;
            Model = job.Model;
            Colour = job.Colour;
            VinNumber = job.VinNumber;
            TrackingUnitMake = job.TrackingUnitMake;
            _suppressIccidAutoLookup = true;
            try
            {
                Imei = job.Imei;
                SerialNumber = job.SerialNumber;
                Iccid = job.Iccid;
                SimNumber = job.SimNumber;
            }
            finally
            {
                _suppressIccidAutoLookup = false;
            }

            if (!string.IsNullOrWhiteSpace(Iccid))
            {
                _ = AutoLookupFromIccidAsync(Iccid);
            }

            logMsg = $"[JobCardEditViewModel] Properties set: Make={Make}, Model={Model}, Imei={Imei}, Iccid={Iccid}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Load models for the selected make
            if (!string.IsNullOrWhiteSpace(Make))
            {
                UpdateAvailableModels();
            }
        }
        else
        {
            var logMsg = $"[JobCardEditViewModel] JobCard {jobCardId} not found!";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
        }
    }

    private void RefreshAvailableMakes()
    {
        AvailableMakes.Clear();
        foreach (var make in _vehicleService.GetAllVehicleMakes())
        {
            AvailableMakes.Add(make);
        }
    }

    private void LoadClients()
    {
        ClientNames.Clear();
        using var db = new AppDbContext();
        foreach (var name in db.Clients
                     .AsNoTracking()
                     .Select(c => c.Name)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct()
                     .OrderBy(n => n)
                     .ToList())
        {
            ClientNames.Add(name);
        }
    }

    private void AddClientNameIfMissing(string? companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return;

        if (ClientNames.Any(x => string.Equals(x, companyName, StringComparison.OrdinalIgnoreCase)))
            return;

        ClientNames.Add(companyName.Trim());
    }

    private void UpdateAvailableModels()
    {
        AvailableModels.Clear();
        if (!string.IsNullOrWhiteSpace(Make))
        {
            foreach (var model in _vehicleService.GetVehicleModelsByMake(Make))
            {
                AvailableModels.Add(model);
            }
        }
    }

    private async Task AutoLookupFromIccidAsync(string? iccidValue)
    {
        var normalized = NormalizeDigits(iccidValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            _lastAutoLookupIccid = null;
            FlickswitchStatusMessage = null;
            FlickswitchRulesText = null;
            FlickswitchBalanceStatusMessage = null;
            ClearSimBalanceData();
            return;
        }

        if (normalized.Length < 5)
        {
            FlickswitchRulesText = null;
            FlickswitchBalanceStatusMessage = null;
            ClearSimBalanceData();
            return;
        }

        if (string.Equals(_lastAutoLookupIccid, normalized, StringComparison.Ordinal))
            return;

        FlickswitchRulesText = null;
        FlickswitchBalanceStatusMessage = null;
        ClearSimBalanceData();

        _flickswitchLookupCts?.Cancel();
        var cts = new CancellationTokenSource();
        _flickswitchLookupCts = cts;

        try
        {
            await Task.Delay(700, cts.Token);
            await LookupSimFromFlickswitchAsync(isAutomatic: true, cts.Token);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task LookupSimFromFlickswitchAsync(bool isAutomatic, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Iccid) && string.IsNullOrWhiteSpace(SimNumber))
        {
            FlickswitchStatusMessage = "Enter ICCID or SIM Number before lookup.";
            return;
        }

        var service = new FlickswitchSimControlService(_appState.Settings);
        if (!service.IsConfigured())
        {
            FlickswitchStatusMessage = "Flickswitch API key is not configured in Settings.";
            return;
        }

        var configuredApiKey = _appState.Settings.FlickswitchApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredApiKey)
            && configuredApiKey.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            FlickswitchStatusMessage = "Flickswitch API key looks invalid (a URL was entered). Update it in Settings.";
            return;
        }

        IsLookingUpFlickswitch = true;
        FlickswitchStatusMessage = isAutomatic
            ? "Looking up SIM in Flickswitch..."
            : null;

        try
        {
            var sim = await service.FindByIccidOrPhoneAsync(Iccid, SimNumber, cancellationToken);
            if (sim == null)
            {
                FlickswitchRulesText = null;
                ClearSimBalanceData();
                FlickswitchStatusMessage = string.IsNullOrWhiteSpace(service.LastError)
                    ? "No matching SIM found in Flickswitch."
                    : $"Flickswitch lookup failed: {service.LastError}";
                return;
            }

            _suppressIccidAutoLookup = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(sim.Iccid))
                    Iccid = sim.Iccid.Trim();
            }
            finally
            {
                _suppressIccidAutoLookup = false;
            }

            if (!string.IsNullOrWhiteSpace(sim.SimNumber))
                SimNumber = sim.SimNumber.Trim();

            var normalizedIccid = NormalizeDigits(Iccid);
            if (!string.IsNullOrWhiteSpace(normalizedIccid))
                _lastAutoLookupIccid = normalizedIccid;

            var distinctRules = sim.Rules
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            FlickswitchRulesText = distinctRules.Count == 0
                ? "No active rules returned by Flickswitch."
                : string.Join(Environment.NewLine, distinctRules.Select(x => $"- {x}"));

            // Try to refresh balances and then fetch latest snapshot.
            var baseline = sim;
            var refresh = await service.RequestSimBalancesRefreshAsync(Iccid, SimNumber, null, cancellationToken);
            sim = await WaitForUpdatedBalanceSnapshotAsync(service, baseline, cancellationToken) ?? sim;
            SimAirtimeBalance = sim.AirtimeBalance;
            SimDataBalanceMb = sim.DataBalanceMb;
            SimSmsBalance = sim.SmsBalance;
            SimLastBalanceCheckAt = sim.LastBalanceCheckAt;

            var hasBalanceData = HasSimBalanceData;
            var hasFreshUpdate = IsBalanceSnapshotFresh(baseline, sim);
            if (refresh.ok)
            {
                FlickswitchBalanceStatusMessage = hasFreshUpdate
                    ? "SIM balances refreshed."
                    : "SIM balances loaded from latest available snapshot.";
            }
            else if (hasBalanceData)
            {
                FlickswitchBalanceStatusMessage = "SIM balances loaded from latest available snapshot.";
            }
            else
            {
                FlickswitchBalanceStatusMessage = string.IsNullOrWhiteSpace(refresh.message)
                    ? "Could not refresh SIM balances."
                    : $"Balance refresh failed: {refresh.message}";
            }

            FlickswitchStatusMessage = string.IsNullOrWhiteSpace(sim.Status)
                ? "SIM details loaded from Flickswitch."
                : $"SIM details loaded from Flickswitch. Status: {sim.Status}";
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            FlickswitchStatusMessage = $"Flickswitch lookup failed: {ex.Message}";
        }
        finally
        {
            IsLookingUpFlickswitch = false;
        }
    }

    private void OnSimBalanceValuesChanged()
    {
        OnPropertyChanged(nameof(HasSimBalanceData));
        OnPropertyChanged(nameof(SimAirtimeBalanceDisplay));
        OnPropertyChanged(nameof(SimDataBalanceDisplay));
        OnPropertyChanged(nameof(SimSmsBalanceDisplay));
        OnPropertyChanged(nameof(SimLastBalanceCheckDisplay));
    }

    private void ClearSimBalanceData()
    {
        SimAirtimeBalance = null;
        SimDataBalanceMb = null;
        SimSmsBalance = null;
        SimLastBalanceCheckAt = null;
    }

    private async Task<FlickswitchSimInfo?> WaitForUpdatedBalanceSnapshotAsync(
        FlickswitchSimControlService service,
        FlickswitchSimInfo? baseline,
        CancellationToken cancellationToken)
    {
        FlickswitchSimInfo? latest = baseline;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, cancellationToken);

            var current = await service.FindByIccidOrPhoneAsync(Iccid, SimNumber, cancellationToken);
            if (current is null)
                continue;

            latest = current;
            if (IsBalanceSnapshotFresh(baseline, current))
                return current;
        }

        return latest;
    }

    private static bool IsBalanceSnapshotFresh(FlickswitchSimInfo? baseline, FlickswitchSimInfo current)
    {
        if (baseline is null)
            return true;

        if (current.LastBalanceCheckAt.HasValue)
        {
            if (!baseline.LastBalanceCheckAt.HasValue)
                return true;

            if (current.LastBalanceCheckAt.Value > baseline.LastBalanceCheckAt.Value)
                return true;
        }

        return current.AirtimeBalance != baseline.AirtimeBalance
               || current.DataBalanceMb != baseline.DataBalanceMb
               || current.SmsBalance != baseline.SmsBalance;
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    [RelayCommand]
    private void Cancel() => _close();

    [RelayCommand]
    private void Save()
    {
        if (!IsEditable)
        {
            _appState.SetStatus("Completed job cards are read-only and cannot be edited.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Company))
        {
            _appState.SetStatus("Company is required.");
            return;
        }

        using var db = new AppDbContext();
        var job = db.JobCards.Find(_jobCardId);
        if (job == null) { _close(); return; }

        job.Company = Company.Trim();
        job.Registration = string.IsNullOrWhiteSpace(Registration) ? "" : Registration.Trim().ToUpperInvariant();
        job.FleetNumber = string.IsNullOrWhiteSpace(FleetNumber) ? null : FleetNumber.Trim();
        job.Make = string.IsNullOrWhiteSpace(Make) ? null : Make.Trim();
        job.Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim();
        job.Colour = string.IsNullOrWhiteSpace(Colour) ? null : Colour.Trim();
        job.VinNumber = string.IsNullOrWhiteSpace(VinNumber) ? null : VinNumber.Trim();
        job.TrackingUnitMake = string.IsNullOrWhiteSpace(TrackingUnitMake) ? null : TrackingUnitMake.Trim();
        job.Imei = string.IsNullOrWhiteSpace(Imei) ? null : Imei.Trim();
        job.SerialNumber = string.IsNullOrWhiteSpace(SerialNumber) ? null : SerialNumber.Trim();
        job.Iccid = string.IsNullOrWhiteSpace(Iccid) ? null : Iccid.Trim();
        job.SimNumber = string.IsNullOrWhiteSpace(SimNumber) ? null : SimNumber.Trim();
        db.SaveChanges();

        _close();
    }
}

