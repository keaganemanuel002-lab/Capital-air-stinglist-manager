using System.Linq;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class QuotePriceResult
{
    public decimal AmountExVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmountIncVat { get; set; }
}

public class QuotePricingService
{
    private readonly AppSettings _settings;

    public QuotePricingService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Calculate the ex-VAT, VAT, and inc-VAT amounts for a quote based on product type and features.
    /// </summary>
    public QuotePriceResult CalculatePrice(Quote quote)
    {
        decimal exVatAmount = CalculateExVatAmount(quote);
        decimal vatAmount = exVatAmount * _settings.VatRate;

        if (quote.LineItems != null && quote.LineItems.Count > 0)
        {
            exVatAmount = quote.LineItems.Sum(x => x.LineTotalExVat);
            var vatBase = quote.LineItems.Where(x => !x.IsVatExempt).Sum(x => x.LineTotalExVat);
            vatAmount = vatBase * _settings.VatRate;
        }

        decimal incVatAmount = exVatAmount + vatAmount;

        return new QuotePriceResult
        {
            AmountExVat = exVatAmount,
            VatAmount = vatAmount,
            AmountIncVat = incVatAmount
        };
    }

    /// <summary>
    /// Calculate ex-VAT amount based on product type and features.
    /// If AmountExVat is already set (non-zero), use that instead.
    /// </summary>
    public decimal CalculateExVatAmount(Quote quote)
    {
        // If a custom amount is already set, use it
        if (quote.AmountExVat > 0)
            return quote.AmountExVat;

        decimal basePrice = GetBasePrice(quote.ProductType);
        decimal addonsPrice = GetAddonsPrice(quote.IncludesPanicButton, quote.IncludesAppLiveTracking);

        return basePrice + addonsPrice;
    }

    private decimal GetBasePrice(string? productType)
    {
        return productType?.Trim().ToLower() switch
        {
            "sting" => _settings.PackagePricing.StingBaseExVat,
            "sting plus" => _settings.PackagePricing.StingPlusBaseExVat,
            "sting fm" => _settings.PackagePricing.StingFmBaseExVat,
            _ => 0m
        };
    }

    private decimal GetAddonsPrice(bool includesPanicButton, bool includesAppLiveTracking)
    {
        decimal addonsPrice = 0m;

        if (includesPanicButton)
            addonsPrice += _settings.PackagePricing.PanicButtonAddonExVat;

        if (includesAppLiveTracking)
            addonsPrice += _settings.PackagePricing.AppLiveTrackingAddonExVat;

        return addonsPrice;
    }

    /// <summary>
    /// Format price for display as "R{amount:0.00}"
    /// </summary>
    public string FormatPrice(decimal amount)
    {
        return $"R{amount:0.00}";
    }
}
