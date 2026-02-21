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

public class QuoteLineItemDisplayRow
{
    public string ProductName { get; set; } = "";
    public string? ProductCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceExVat { get; set; }
    public decimal LineTotalExVat { get; set; }
}

public partial class QuoteDetailsViewModel : ViewModelBase
{
    private readonly Action _closeAction;
    private readonly AppState _appState;

    public string Title => $"Quote {QuoteReference}";
    public string QuoteReference => QuoteReferenceFormatter.Format(QuoteNumber);

    [ObservableProperty] private int quoteNumber;
    [ObservableProperty] private string quoteType = "";
    [ObservableProperty] private string quoteStatus = "";
    [ObservableProperty] private string company = "";
    [ObservableProperty] private string? registration;
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? vehicleDescription;
    [ObservableProperty] private string? vinNumber;
    [ObservableProperty] private string? trackingUnitMake;
    [ObservableProperty] private string? imei;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? iccid;
    [ObservableProperty] private string? simNumber;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private DateTime createdAt;
    
    [ObservableProperty] private decimal subtotalExVat;
    [ObservableProperty] private decimal vatAmount;
    [ObservableProperty] private decimal totalIncVat;

    [ObservableProperty] private bool hasJobCard;
    [ObservableProperty] private int jobCardNumber;
    [ObservableProperty] private string jobCardReference = "";
    [ObservableProperty] private string? jobType;
    [ObservableProperty] private string? jobStatus;
    [ObservableProperty] private DateTime? jobCreatedAt;
    [ObservableProperty] private DateTime? jobCompletedAt;
    [ObservableProperty] private DateTime? scheduledFor;

    public bool HasSchedule => ScheduledFor.HasValue;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public ObservableCollection<QuoteLineItemDisplayRow> LineItems { get; } = new();

    public QuoteDetailsViewModel(Action closeAction, int quoteId, AppState appState)
    {
        _closeAction = closeAction;
        _appState = appState;
        LoadQuoteDetails(quoteId);
    }

    private void LoadQuoteDetails(int quoteId)
    {
        using var db = new AppDbContext();
        
        var quote = db.Quotes
            .Include(q => q.LineItems)
            .FirstOrDefault(q => q.Id == quoteId);

        if (quote == null)
        {
            _appState.SetStatus("Quote not found.");
            return;
        }

        // Populate quote details
        QuoteNumber = quote.QuoteNumber;
        QuoteType = quote.Type.ToString();
        QuoteStatus = quote.Status.ToString();
        Company = quote.Company;
        Registration = quote.Registration;
        FleetNumber = quote.FleetNumber;
        VinNumber = quote.VinNumber;
        TrackingUnitMake = quote.TrackingUnitMake;
        Imei = quote.Imei;
        SerialNumber = quote.SerialNumber;
        Iccid = quote.Iccid;
        SimNumber = quote.SimNumber;
        Notes = quote.Notes;
        CreatedAt = quote.CreatedAt;

        // Build vehicle description
        if (!string.IsNullOrWhiteSpace(quote.Make) || !string.IsNullOrWhiteSpace(quote.Model))
        {
            var parts = new[] { quote.Make, quote.Model, quote.Colour }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            VehicleDescription = string.Join(" ", parts);
        }

        // Populate line items and calculate totals
        LineItems.Clear();
        foreach (var item in quote.LineItems.OrderBy(x => x.LineNumber))
        {
            LineItems.Add(new QuoteLineItemDisplayRow
            {
                ProductName = item.ProductName,
                ProductCode = item.ProductCode,
                Quantity = item.Quantity,
                UnitPriceExVat = item.UnitPriceExVat,
                LineTotalExVat = item.LineTotalExVat
            });
        }

        // Calculate totals
        var pricingService = new QuotePricingService(_appState.Settings);
        var priceResult = pricingService.CalculatePrice(quote);
        SubtotalExVat = quote.AmountExVat;
        VatAmount = priceResult.VatAmount;
        TotalIncVat = priceResult.AmountIncVat;

        // Load job card if exists
        var jobCard = db.JobCards
            .FirstOrDefault(j => j.QuoteId == quoteId);

        if (jobCard != null)
        {
            HasJobCard = true;
            JobCardNumber = jobCard.JobCardNumber;
            JobCardReference = JobCardReferenceFormatter.Format(jobCard.Type, jobCard.JobCardNumber);
            JobType = jobCard.Type.ToString();
            JobStatus = jobCard.Status.ToString();
            JobCreatedAt = jobCard.CreatedAt;
            JobCompletedAt = jobCard.CompletedAt;
            ScheduledFor = jobCard.ScheduledFor;
            OnPropertyChanged(nameof(HasSchedule));
        }
        else
        {
            HasJobCard = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _closeAction();
    }

    partial void OnQuoteNumberChanged(int value)
    {
        OnPropertyChanged(nameof(QuoteReference));
        OnPropertyChanged(nameof(Title));
    }
}
