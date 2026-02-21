using System;

namespace StingListManager.Data.Entities;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameNorm { get; set; } = "";
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    public string? EmailAddress { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
