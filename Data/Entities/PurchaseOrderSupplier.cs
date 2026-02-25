namespace StingListManager.Data.Entities;

public class PurchaseOrderSupplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameNorm { get; set; } = string.Empty;
    public bool QuoteIncludesVatDefault { get; set; }
}
