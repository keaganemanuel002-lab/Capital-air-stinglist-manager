namespace StingListManager.Data.Entities;

public class PurchaseOrderLineItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal AmountExVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmountIncVat { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
}
