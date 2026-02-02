using System;

namespace StingListManager.Data.Entities;

public class AuditEvent
{
    public int Id { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = ""; // who did it

    public string Action { get; set; } = ""; // e.g. "IMPORT", "BILLING_ADD", "REMOVAL_COMPLETE"
    public string EntityType { get; set; } = ""; // "BillingEntry", "JobCard", etc.
    public int? EntityId { get; set; }

    public string? Registration { get; set; }

    public string? Details { get; set; } // free text JSON-ish if needed later
}
