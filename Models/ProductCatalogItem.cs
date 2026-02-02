using System;

namespace StingListManager.Models;

public class ProductCatalogItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal BasePriceExVat { get; set; }
    public decimal PanicButtonAddonExVat { get; set; }
    public decimal AppLiveTrackingAddonExVat { get; set; }
    public bool IsVatExempt { get; set; }
}
