using System;

namespace StingListManager.Data.Entities;

public enum CancellationStatus
{
    Requested = 0,
    Quoted = 1,
    Approved = 2,
    JobCreated = 3,
    Completed = 4
}

public class CancellationEntry
{
    public int Id { get; set; }

    // Client/vehicle
    public string Client { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string? MakeModel { get; set; }

    // Unit identification (important!)
    public string? UnitModel { get; set; }

    // Process tracking
    public DateTime? DateRequestReceived { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public CancellationStatus Status { get; set; } = CancellationStatus.Requested;

    // Link to workflow
    public int? QuoteId { get; set; }
    public int? JobCardId { get; set; }
}
