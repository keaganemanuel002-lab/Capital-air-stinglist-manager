using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class RelatedQuoteRow : ObservableObject
{
    public int QuoteNumber { get; set; }
    public string QuoteReference => QuoteReferenceFormatter.Format(QuoteNumber);
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal AmountIncVat { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class InstallationDetailsViewModel : ViewModelBase
{
    private readonly Action _closeAction;
    private readonly AppState _appState;

    public string Title { get; }

    [ObservableProperty] private string company = "";
    [ObservableProperty] private string registration = "";
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? vehicleDescription;
    [ObservableProperty] private string? vinNumber;
    [ObservableProperty] private string? trackingUnitMake;
    [ObservableProperty] private string? imei;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? iccid;
    [ObservableProperty] private string? simNumber;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private DateTime activeFrom;
    [ObservableProperty] private decimal? simAirtimeBalance;
    [ObservableProperty] private decimal? simDataBalanceMb;
    [ObservableProperty] private decimal? simSmsBalance;
    [ObservableProperty] private DateTimeOffset? simLastBalanceCheckAt;
    [ObservableProperty] private bool isLoadingSimBalances;
    [ObservableProperty] private string? simBalanceStatusMessage;

    [ObservableProperty] private bool hasInstallJobCard;
    [ObservableProperty] private int installJobCardNumber;
    [ObservableProperty] private string installJobCardReference = "";
    [ObservableProperty] private string? installJobStatus;
    [ObservableProperty] private DateTime? installScheduledFor;
    [ObservableProperty] private DateTime? installCompletedAt;

    public bool HasRelatedQuotes => RelatedQuotes.Count > 0;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasSimBalanceData =>
        SimAirtimeBalance.HasValue
        || SimDataBalanceMb.HasValue
        || SimSmsBalance.HasValue
        || SimLastBalanceCheckAt.HasValue;
    public string SimAirtimeBalanceDisplay => SimAirtimeBalance.HasValue ? $"R {SimAirtimeBalance.Value:0.00}" : "-";
    public string SimDataBalanceDisplay => SimDataBalanceMb.HasValue ? $"{SimDataBalanceMb.Value:0.##} MB" : "-";
    public string SimSmsBalanceDisplay => SimSmsBalance.HasValue ? $"{SimSmsBalance.Value:0.##}" : "-";
    public string SimLastBalanceCheckDisplay => SimLastBalanceCheckAt.HasValue ? SimLastBalanceCheckAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";

    public ObservableCollection<RelatedQuoteRow> RelatedQuotes { get; } = new();

    public InstallationDetailsViewModel(Action closeAction, int billingEntryId, AppState appState)
    {
        _closeAction = closeAction;
        _appState = appState;
        Title = "Installation Details";
        LoadInstallationDetails(billingEntryId);
        _ = LoadSimBalancesAsync();
    }

    private void LoadInstallationDetails(int billingEntryId)
    {
        using var db = new AppDbContext();
        
        var entry = db.BillingEntries.FirstOrDefault(b => b.Id == billingEntryId);
        if (entry == null)
        {
            _appState.SetStatus("Billing entry not found.");
            return;
        }

        // Populate installation details
        Company = entry.Company;
        Registration = entry.Registration;
        FleetNumber = entry.FleetNumber;
        VinNumber = entry.VinNumber;
        TrackingUnitMake = entry.TrackingUnitMake;
        Imei = entry.Imei;
        SerialNumber = entry.SerialNumber;
        Iccid = entry.Iccid;
        SimNumber = entry.SimNumber;
        Notes = entry.Notes;
        Status = entry.Status.ToDisplayString();
        ActiveFrom = entry.ActiveFrom;

        // Build vehicle description
        if (!string.IsNullOrWhiteSpace(entry.Make) || !string.IsNullOrWhiteSpace(entry.Model))
        {
            var parts = new[] { entry.Make, entry.Model, entry.Colour }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            VehicleDescription = string.Join(" ", parts);
        }

        // Find related installation job cards first (best link to quote via QuoteId).
        var installJobs = db.JobCards
            .AsNoTracking()
            .Where(j => j.Type == JobType.Install)
            .OrderByDescending(j => j.CreatedAt)
            .ToList()
            .Where(j => IsJobRelatedToBillingEntry(j, entry))
            .ToList();

        var installJob = installJobs.FirstOrDefault();
        if (installJob != null)
        {
            HasInstallJobCard = true;
            InstallJobCardNumber = installJob.JobCardNumber;
            InstallJobCardReference = JobCardReferenceFormatter.Format(installJob.Type, installJob.JobCardNumber);
            InstallJobStatus = installJob.Status.ToString();
            InstallScheduledFor = installJob.ScheduledFor;
            InstallCompletedAt = installJob.CompletedAt;
        }

        var relatedQuoteIds = installJobs
            .Where(j => j.QuoteId.HasValue)
            .Select(j => j.QuoteId!.Value)
            .ToHashSet();

        // Fallback links when QuoteId is missing: company/registration matching.
        if (!string.IsNullOrWhiteSpace(entry.Registration))
        {
            var registration = entry.Registration.Trim();
            var company = entry.Company.Trim();
            var fallbackQuoteIds = db.Quotes
                .AsNoTracking()
                .Where(q =>
                    (!string.IsNullOrWhiteSpace(q.Registration) && q.Registration == registration)
                    || (q.Company == company && q.Registration == registration))
                .Select(q => q.Id)
                .ToList();

            foreach (var quoteId in fallbackQuoteIds)
                relatedQuoteIds.Add(quoteId);
        }

        if (relatedQuoteIds.Count > 0)
        {
            var quotes = db.Quotes
                .AsNoTracking()
                .Where(q => relatedQuoteIds.Contains(q.Id))
                .OrderByDescending(q => q.CreatedAt)
                .ToList();

            var pricingService = new QuotePricingService(_appState.Settings);
            foreach (var quote in quotes)
            {
                var priceResult = pricingService.CalculatePrice(quote);
                RelatedQuotes.Add(new RelatedQuoteRow
                {
                    QuoteNumber = quote.QuoteNumber,
                    Type = quote.Type.ToString(),
                    Status = quote.Status.ToString(),
                    AmountIncVat = priceResult.AmountIncVat,
                    CreatedAt = quote.CreatedAt
                });
            }
        }

        OnPropertyChanged(nameof(HasRelatedQuotes));
    }

    private static bool IsJobRelatedToBillingEntry(JobCard job, BillingEntry entry)
    {
        var entryImei = NormalizeDigits(entry.Imei);
        var jobImei = NormalizeDigits(job.Imei);
        var imeiMatch = !string.IsNullOrWhiteSpace(entryImei)
                        && !string.IsNullOrWhiteSpace(jobImei)
                        && (entryImei == jobImei || entryImei.EndsWith(jobImei, StringComparison.Ordinal) || jobImei.EndsWith(entryImei, StringComparison.Ordinal));

        var serialMatch = !string.IsNullOrWhiteSpace(entry.SerialNumber)
                          && !string.IsNullOrWhiteSpace(job.SerialNumber)
                          && string.Equals(entry.SerialNumber.Trim(), job.SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase);

        var registrationMatch = !string.IsNullOrWhiteSpace(entry.Registration)
                                && !string.IsNullOrWhiteSpace(job.Registration)
                                && string.Equals(entry.Registration.Trim(), job.Registration.Trim(), StringComparison.OrdinalIgnoreCase);

        var companyRegMatch = registrationMatch
                              && !string.IsNullOrWhiteSpace(entry.Company)
                              && !string.IsNullOrWhiteSpace(job.Company)
                              && string.Equals(entry.Company.Trim(), job.Company.Trim(), StringComparison.OrdinalIgnoreCase);

        return imeiMatch || serialMatch || companyRegMatch;
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    partial void OnSimAirtimeBalanceChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimDataBalanceMbChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimSmsBalanceChanged(decimal? value) => OnSimBalanceValuesChanged();
    partial void OnSimLastBalanceCheckAtChanged(DateTimeOffset? value) => OnSimBalanceValuesChanged();

    private void OnSimBalanceValuesChanged()
    {
        OnPropertyChanged(nameof(HasSimBalanceData));
        OnPropertyChanged(nameof(SimAirtimeBalanceDisplay));
        OnPropertyChanged(nameof(SimDataBalanceDisplay));
        OnPropertyChanged(nameof(SimSmsBalanceDisplay));
        OnPropertyChanged(nameof(SimLastBalanceCheckDisplay));
    }

    private async Task LoadSimBalancesAsync()
    {
        if (string.IsNullOrWhiteSpace(Iccid) && string.IsNullOrWhiteSpace(SimNumber))
        {
            SimBalanceStatusMessage = "No ICCID or SIM Number available for balance lookup.";
            return;
        }

        var service = new FlickswitchSimControlService(_appState.Settings);
        if (!service.IsConfigured())
        {
            SimBalanceStatusMessage = "Flickswitch is not configured in Settings.";
            return;
        }

        IsLoadingSimBalances = true;
        SimBalanceStatusMessage = "Loading SIM balance information...";

        try
        {
            var initialSim = await service.FindByIccidOrPhoneAsync(Iccid, SimNumber);
            var refresh = await service.RequestSimBalancesRefreshAsync(Iccid, SimNumber, null);
            var sim = await WaitForUpdatedBalanceSnapshotAsync(service, initialSim);

            if (sim is null)
            {
                SimBalanceStatusMessage = string.IsNullOrWhiteSpace(service.LastError)
                    ? "SIM not found in Flickswitch."
                    : $"Flickswitch lookup failed: {service.LastError}";
                return;
            }

            SimAirtimeBalance = sim.AirtimeBalance;
            SimDataBalanceMb = sim.DataBalanceMb;
            SimSmsBalance = sim.SmsBalance;
            SimLastBalanceCheckAt = sim.LastBalanceCheckAt;
            var hasBalanceData = SimAirtimeBalance.HasValue || SimDataBalanceMb.HasValue || SimSmsBalance.HasValue || SimLastBalanceCheckAt.HasValue;
            var hasFreshUpdate = IsBalanceSnapshotFresh(initialSim, sim);

            if (refresh.ok)
            {
                SimBalanceStatusMessage = hasFreshUpdate
                    ? "SIM balances refreshed and loaded."
                    : "SIM balances loaded from latest available snapshot.";
                return;
            }

            if (hasBalanceData)
            {
                SimBalanceStatusMessage = "SIM balances loaded from latest available snapshot.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(refresh.message))
            {
                SimBalanceStatusMessage = $"Balance refresh failed: {refresh.message}";
                return;
            }

            SimBalanceStatusMessage = "SIM balances loaded.";
        }
        catch (Exception ex)
        {
            SimBalanceStatusMessage = $"SIM balance lookup failed: {ex.Message}";
        }
        finally
        {
            IsLoadingSimBalances = false;
        }
    }

    private async Task<FlickswitchSimInfo?> WaitForUpdatedBalanceSnapshotAsync(
        FlickswitchSimControlService service,
        FlickswitchSimInfo? baseline)
    {
        FlickswitchSimInfo? latest = baseline;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1200);

            var current = await service.FindByIccidOrPhoneAsync(Iccid, SimNumber);
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

    [RelayCommand]
    private void Close()
    {
        _closeAction();
    }
}
