using System;

namespace StingListManager.Data.Entities;

public enum JobType { Install = 0, Removal = 1, Transfer = 2 }
public enum JobStatus { Open = 0, Completed = 1, Cancelled = 2 }

public class JobCard
{
    public int Id { get; set; }

    public int JobCardNumber { get; set; }
    public JobType Type { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Open;

    // Linked quote (optional but useful)
    public int? QuoteId { get; set; }
    public Quote? Quote { get; set; }

    // Same vehicle info
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Colour { get; set; }
    public string? VinNumber { get; set; }
    public string? GridLocation { get; set; }

    // Tracking unit info (for installs, and for selecting which unit to remove)
    public string? TrackingUnitMake { get; set; }
    public string? Notes { get; set; }

    // Teltonika device information
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? SimNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledFor { get; set; }
    public DateTime? CompletedAt { get; set; }
}
