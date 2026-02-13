using System;
using System.Collections.ObjectModel;
using System.Linq;
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

    [ObservableProperty] private bool hasInstallJobCard;
    [ObservableProperty] private int installJobCardNumber;
    [ObservableProperty] private string? installJobStatus;
    [ObservableProperty] private DateTime? installScheduledFor;
    [ObservableProperty] private DateTime? installCompletedAt;

    public bool HasRelatedQuotes => RelatedQuotes.Count > 0;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public ObservableCollection<RelatedQuoteRow> RelatedQuotes { get; } = new();

    public InstallationDetailsViewModel(Action closeAction, int billingEntryId, AppState appState)
    {
        _closeAction = closeAction;
        _appState = appState;
        Title = "Installation Details";
        LoadInstallationDetails(billingEntryId);
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

        // Find related quotes by matching registration
        if (!string.IsNullOrWhiteSpace(entry.Registration))
        {
            var quotes = db.Quotes
                .Where(q => q.Registration == entry.Registration)
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

            OnPropertyChanged(nameof(HasRelatedQuotes));

            // Find installation job card - look for Install type job cards matching this registration
            var installJob = db.JobCards
                .Where(j => j.Registration == entry.Registration && j.Type == JobType.Install)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefault();

            if (installJob != null)
            {
                HasInstallJobCard = true;
                InstallJobCardNumber = installJob.JobCardNumber;
                InstallJobStatus = installJob.Status.ToString();
                InstallScheduledFor = installJob.ScheduledFor;
                InstallCompletedAt = installJob.CompletedAt;
            }
        }
    }

    [RelayCommand]
    private void Close()
    {
        _closeAction();
    }
}
