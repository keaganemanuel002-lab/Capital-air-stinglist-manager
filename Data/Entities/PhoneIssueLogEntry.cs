using System;

namespace StingListManager.Data.Entities;

public class PhoneIssueLogEntry
{
    public int Id { get; set; }

    public string TeamName { get; set; } = "";
    public string VehicleRegistration { get; set; } = "";
    public string TeamMemberOne { get; set; } = "";
    public string TeamMemberTwo { get; set; } = "";

    public string? PhoneLabel { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhoneImei { get; set; }
    public string? PhoneImeiSecondary { get; set; }
    public string? RepairDetails { get; set; }
    public string? Notes { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReturnedAt { get; set; }

    public string TeamNameNorm { get; set; } = "";
    public string VehicleRegistrationNorm { get; set; } = "";
    public string PhoneImeiNorm { get; set; } = "";
    public string PhoneImeiSecondaryNorm { get; set; } = "";
}
