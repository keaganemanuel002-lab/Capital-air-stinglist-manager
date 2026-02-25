using System;

namespace StingListManager.Data.Entities;

public class DriverTagTransfer
{
    public int Id { get; set; }

    public int DriverTagId { get; set; }

    public string FromDriverName { get; set; } = "";
    public string ToDriverName { get; set; } = "";
    public string Reason { get; set; } = "";

    public DateTime TransferredAt { get; set; } = DateTime.UtcNow;
    public string? TransferredBy { get; set; }
}
