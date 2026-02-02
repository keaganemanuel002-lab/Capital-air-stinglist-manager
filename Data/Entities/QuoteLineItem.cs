using System;

namespace StingListManager.Data.Entities;

public class QuoteLineItem
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public int LineNumber { get; set; }
    public string ProductType { get; set; } = ""; // Legacy display name
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IncludesPanicButton { get; set; }
    public bool IncludesAppLiveTracking { get; set; }
    
    public decimal UnitPriceExVat { get; set; }
    public decimal LineTotalExVat { get; set; }
    public bool IsVatExempt { get; set; }
    
    public string? Description { get; set; } // Optional custom description
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
