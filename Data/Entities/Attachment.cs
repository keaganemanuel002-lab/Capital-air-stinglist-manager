using System;

namespace StingListManager.Data.Entities;

public enum AttachmentOwnerType
{
    Quote = 0,
    JobCard = 1
}

public enum AttachmentKind
{
    QuotePdf = 0,
    QuoteSigned = 1,
    Invoice = 2,
    JobPhoto = 3,
    Other = 9
}

public class Attachment
{
    public int Id { get; set; }

    public AttachmentOwnerType OwnerType { get; set; }
    public int OwnerId { get; set; }              // Quote.Id or JobCard.Id
    public AttachmentKind Kind { get; set; }      // signed quote, invoice, etc.

    public string FileName { get; set; } = "";    // display name
    public string StoredPath { get; set; } = "";  // physical path on disk
    public string? Notes { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string AddedBy { get; set; } = "";     // operator name
}
