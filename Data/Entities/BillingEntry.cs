using System;

namespace StingListManager.Data.Entities;

public enum BillingStatus
{
    Active = 0,
    Removed = 1,
    NotLoaded = 2
}

public static class BillingStatusExtensions
{
    public static string ToDisplayString(this BillingStatus status)
    {
        return status switch
        {
            BillingStatus.NotLoaded => "Not Loaded",
            _ => status.ToString()
        };
    }
}

public class BillingEntry
{
    public int Id { get; set; }

    // Excel columns you already have
    public string Company { get; set; } = "";          // COMPANY
    public string Registration { get; set; } = "";     // REG.
    public string? FleetNumber { get; set; }           // FLT. NO
    public string? Make { get; set; }                  // MAKE
    public string? Model { get; set; }                 // MODEL
    public string? Colour { get; set; }                // COLOUR
    public string? VinNumber { get; set; }             // VIN
    public string? TrackingUnitMake { get; set; }      // TRACKING UNIT MAKE
    public string? StingPackageType { get; set; }      // STING / STING PLUS / STING FM
    public string? Notes { get; set; }                 // NOTES
    public string? Reason { get; set; }                // Reason

    // Teltonika device information
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? SimNumber { get; set; }

    // Lifecycle
    public BillingStatus Status { get; set; } = BillingStatus.Active;
    public DateTime ActiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ActiveTo { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // Normalized fields for search
    public string RegistrationNorm { get; set; } = ""; // uppercase trimmed
    public string ImeiNorm { get; set; } = ""; // digits-only
    public string IccidNorm { get; set; } = ""; // digits-only
    public string SerialNumberNorm { get; set; } = ""; // uppercase trimmed
}
