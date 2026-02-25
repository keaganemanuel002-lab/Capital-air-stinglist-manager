namespace StingListManager.Services;

public static class FirestoreCollections
{
    public const string Clients = "clients";
    public const string BillingEntries = "billing_entries";
    public const string JobCards = "job_cards";
    public const string Quotes = "quotes";
    public const string Attachments = "attachments";
    public const string Sequences = "sequences";

    // Existing hybrid sync collections kept for compatibility.
    public const string OpenJobCards = "job_cards_open";
    public const string CompletedJobCards = "job_cards_completed";
    public const string PhotoSubmissions = "photo_submissions";
    public const string MobileUsers = "mobile_users";
}
