using System;
using System.Collections.Generic;

namespace StingListManager.Data.Entities;

public class PurchaseOrder
{
    public int Id { get; set; }
    public int OrderNumber { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal AmountExVat { get; set; }
    public decimal VatRate { get; set; } = 0.15m;
    public decimal VatAmount { get; set; }
    public decimal TotalAmountIncVat { get; set; }
    public bool QuoteIncludesVat { get; set; }
    public string Status { get; set; } = "Draft";
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public List<PurchaseOrderLineItem> LineItems { get; set; } = new();
}
