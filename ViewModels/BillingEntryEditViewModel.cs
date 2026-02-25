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

public partial class BillingEntryEditViewModel : ViewModelBase
{
    private readonly int? _billingEntryId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;
    private bool _suppressIccidAutoLookup;
    private string? _lastAutoLookupIccid;
    private CancellationTokenSource? _flickswitchLookupCts;

    [ObservableProperty] private string windowTitle = "Edit Billing Entry";
    [ObservableProperty] private string company = string.Empty;
    [ObservableProperty] private string registration = string.Empty;
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? make;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? colour;
    [ObservableProperty] private string? vinNumber;
    [ObservableProperty] private string? trackingUnitMake;
    [ObservableProperty] private string? stingPackageType;
    [ObservableProperty] private string? imei;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? iccid;
    [ObservableProperty] private string? simNumber;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? reason;
    [ObservableProperty] private string? flickswitchStatusMessage;
    [ObservableProperty] private string? flickswitchRulesText;
    [ObservableProperty] private decimal? simAirtimeBalance;
    [ObservableProperty] private decimal? simDataBalanceMb;
    [ObservableProperty] private decimal? simSmsBalance;
    [ObservableProperty] private DateTimeOffset? simLastBalanceCheckAt;
    [ObservableProperty] private string? flickswitchBalanceStatusMessage;
    [ObservableProperty] private bool isLookingUpFlickswitch;
    [ObservableProperty] private string saveButtonText = "Save Changes";
    [ObservableProperty] private string? errorMessage;

    public ObservableCollection<string> TrackingUnitMakeOptions { get; } = new();
    public ObservableCollection<string> PackageTypeOptions { get; } = new();

    public bool HasFlickswitchRules => !string.IsNullOrWhiteSpace(FlickswitchRulesText);
    public bool HasSimBalanceData =>
        SimAirtimeBalance.HasValue
        || SimDataBalanceMb.HasValue
        || SimSmsBalance.HasValue
        || SimLastBalanceCheckAt.HasValue;
    public string SimAirtimeBalanceDisplay => SimAirtimeBalance.HasValue ? $"R {SimAirtimeBalance.Value:0.00}" : "-";
    public string SimDataBalanceDisplay => SimDataBalanceMb.HasValue ? $"{SimDataBalanceMb.Value:0.##} MB" : "-";
    public string SimSmsBalanceDisplay => SimSmsBalance.HasValue ? $"{SimSmsBalance.Value:0.##}" : "-";
    public string SimLastBalanceCheckDisplay => SimLastBalanceCheckAt.HasValue
        ? SimLastBalanceCheckAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "-";

    public BillingEntryEditViewModel(int billingEntryId, Action close, Action onSaved, AppState appState)
    {
        _billingEntryId = billingEntryId;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;

        ReplaceOptions(TrackingUnitMakeOptions, TrackingUnitMakeCatalog.Options);
        ReplaceOptions(PackageTypeOptions, StingPackageCatalog.Options);
        Load();
    }

    public BillingEntryEditViewModel(Action close, Action onSaved, AppState appState)
    {
        _billingEntryId = null;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;

        WindowTitle = "Add Billing Entry";
        SaveButtonText = "Add Entry";

        ReplaceOptions(TrackingUnitMakeOptions, TrackingUnitMakeCatalog.Options);
        ReplaceOptions(PackageTypeOptions, StingPackageCatalog.Options);
        Load();
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

    private void Load()
    {
        if (_billingEntryId is null)
            return;

        using var db = new AppDbContext();
        var entry = db.BillingEntries.AsNoTracking().FirstOrDefault(x => x.Id == _billingEntryId.Value);
        if (entry is null)
        {
            ErrorMessage = "Billing entry not found.";
            return;
        }

        Company = entry.Company;
        Registration = entry.Registration;
        FleetNumber = entry.FleetNumber;
        Make = entry.Make;
        Model = entry.Model;
        Colour = entry.Colour;
        VinNumber = entry.VinNumber;
        TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(entry.TrackingUnitMake);
        StingPackageType = StingPackageCatalog.Normalize(entry.StingPackageType)
            ?? ResolvePackageTypeFallback(entry.TrackingUnitMake, entry.Notes, entry.Reason);
        ReplaceOptions(TrackingUnitMakeOptions, TrackingUnitMakeCatalog.BuildOptionsIncluding(TrackingUnitMake));
        ReplaceOptions(PackageTypeOptions, StingPackageCatalog.BuildOptionsIncluding(StingPackageType));
        Imei = entry.Imei;
        SerialNumber = entry.SerialNumber;
        _suppressIccidAutoLookup = true;
        try
        {
            Iccid = entry.Iccid;
            SimNumber = entry.SimNumber;
        }
        finally
        {
            _suppressIccidAutoLookup = false;
        }
        Notes = entry.Notes;
        Reason = entry.Reason;

        if (!string.IsNullOrWhiteSpace(Iccid))
        {
            _ = AutoLookupFromIccidAsync(Iccid);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _flickswitchLookupCts?.Cancel();
        _close();
    }

    [RelayCommand]
    private async Task LookupSim()
    {
        _flickswitchLookupCts?.Cancel();
        await LookupSimFromFlickswitchAsync(isAutomatic: false, CancellationToken.None);
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        var normalizedCompany = NormalizeText(Company);
        var normalizedRegistration = NormalizeText(Registration).ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCompany))
        {
            ErrorMessage = "Company is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedRegistration))
        {
            ErrorMessage = "Registration is required.";
            return;
        }

        using var db = new AppDbContext();
        BillingEntry entry;
        if (_billingEntryId is int existingId)
        {
            var existing = db.BillingEntries.FirstOrDefault(x => x.Id == existingId);
            if (existing is null)
            {
                ErrorMessage = "Billing entry no longer exists.";
                return;
            }

            entry = existing;
        }
        else
        {
            entry = new BillingEntry
            {
                ActiveFrom = DateTime.UtcNow
            };
            db.BillingEntries.Add(entry);
        }

        entry.Company = normalizedCompany;
        entry.Registration = normalizedRegistration;
        entry.FleetNumber = TrimOrNull(FleetNumber);
        entry.Make = TrimOrNull(Make);
        entry.Model = TrimOrNull(Model);
        entry.Colour = TrimOrNull(Colour);
        entry.VinNumber = TrimOrNull(VinNumber);
        entry.TrackingUnitMake = TrackingUnitMakeCatalog.Normalize(TrackingUnitMake);
        entry.StingPackageType = StingPackageCatalog.Normalize(StingPackageType)
            ?? ResolvePackageTypeFallback(entry.TrackingUnitMake, Notes, Reason);
        entry.Imei = TrimOrNull(Imei);
        entry.SerialNumber = TrimOrNull(SerialNumber);
        entry.Iccid = TrimOrNull(Iccid);
        entry.SimNumber = TrimOrNull(SimNumber);
        entry.Notes = TrimOrNull(Notes);
        entry.Reason = TrimOrNull(Reason);

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "Could not save. Another active entry already uses this registration/IMEI/ICCID/serial.";
            return;
        }

        _appState.SetStatus(_billingEntryId is int
            ? $"Billing entry updated: {entry.Company} / {entry.Registration}"
            : $"Billing entry added: {entry.Company} / {entry.Registration}");
        _onSaved();
        _close();
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

    private static string? ResolvePackageTypeFallback(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = StingPackageCatalog.Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IReadOnlyCollection<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            target.Add(value);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
