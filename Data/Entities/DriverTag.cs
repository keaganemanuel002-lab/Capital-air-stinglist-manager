using System;

namespace StingListManager.Data.Entities;

public enum DriverEmploymentExitType
{
    None = 0,
    Resigned = 1,
    Fired = 2
}

public enum DriverTagReturnStatus
{
    Unknown = 0,
    Returned = 1,
    NotReturned = 2
}

public class DriverTag
{
    public int Id { get; set; }

    public string TagCode { get; set; } = "";
    public string TagCodeNorm { get; set; } = "";

    public string DriverName { get; set; } = "";
    public string DriverNameNorm { get; set; } = "";

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LostOrDamagedReportedAt { get; set; }
    public string? LostOrDamagedReason { get; set; }

    public DriverEmploymentExitType EmploymentExitType { get; set; } = DriverEmploymentExitType.None;
    public DateTime? EmploymentExitAt { get; set; }

    public DriverTagReturnStatus ReturnStatus { get; set; } = DriverTagReturnStatus.Unknown;
    public DateTime? ReturnedAt { get; set; }

    public string? Notes { get; set; }
}
