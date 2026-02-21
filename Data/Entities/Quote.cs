using System.Collections.Generic;
using System;

namespace StingListManager.Data.Entities;

public enum QuoteType { Install = 0, Removal = 1 }
public enum QuoteStatus { Draft = 0, Sent = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

public class Quote
{
    public int Id { get; set; }

    public int QuoteNumber { get; set; }
    public QuoteType Type { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    // Client + vehicle info (simple now; normalize later if you want)
    public string Company { get; set; } = "";
    public string? Registration { get; set; } // Required for removal quotes, optional for install
    public string? FleetNumber { get; set; }
    public string? ProductType { get; set; } // STING, STING Plus, STING FM, etc.
    public bool IncludesPanicButton { get; set; } // For STING Plus
    public bool IncludesAppLiveTracking { get; set; } // Optional feature
    
    // Vehicle and device details (populated from STING list for removals)
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Colour { get; set; }
    public string? VinNumber { get; set; }
    public string? TrackingUnitMake { get; set; }
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? SimNumber { get; set; }
    
    // Commercials
    public decimal AmountExVat { get; set; }
    public string? Notes { get; set; }
    
    // Line items (new line-based structure)
    public List<QuoteLineItem> LineItems { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
}
