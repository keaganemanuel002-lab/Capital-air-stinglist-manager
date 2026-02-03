using System;

namespace StingListManager.Data.Entities;

public class ClientQuoteSummary
{
    public int Id { get; set; }
    public string Company { get; set; } = "";
    public int StingCount { get; set; }
    public int StingPlusCount { get; set; }
    public int StingFmCount { get; set; }
    public bool HasLiveTracking { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}