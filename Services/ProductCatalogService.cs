using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StingListManager.Models;

namespace StingListManager.Services;

public class ProductCatalogService
{
    private readonly AppSettings _settings;

    public ProductCatalogService(AppSettings settings)
    {
        _settings = settings;
    }

    public List<ProductCatalogItem> LoadCatalog()
    {
        Paths.Ensure();

        if (!File.Exists(Paths.ProductCatalogPath))
        {
            var defaults = BuildDefaultCatalog();
            SaveCatalog(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(Paths.ProductCatalogPath);
            var items = JsonSerializer.Deserialize<List<ProductCatalogItem>>(json) ?? new List<ProductCatalogItem>();
            var updated = EnsureRequiredProducts(items);
            if (updated)
                SaveCatalog(items);
            return items.OrderBy(x => x.Name).ToList();
        }
        catch
        {
            var defaults = BuildDefaultCatalog();
            SaveCatalog(defaults);
            return defaults;
        }
    }

    public void SaveCatalog(List<ProductCatalogItem> items)
    {
        Paths.Ensure();
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Paths.ProductCatalogPath, json);
    }

    public ProductCatalogItem? FindByCode(IEnumerable<ProductCatalogItem> items, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return items.FirstOrDefault(x => Normalize(x.Code) == Normalize(code));
    }

    public ProductCatalogItem? FindByName(IEnumerable<ProductCatalogItem> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return items.FirstOrDefault(x => Normalize(x.Name) == Normalize(name));
    }

    private List<ProductCatalogItem> BuildDefaultCatalog()
    {
        return new List<ProductCatalogItem>
        {
            new()
            {
                Code = "STING",
                Name = "STING",
                Description = "Standard STING package",
                BasePriceExVat = _settings.PackagePricing.StingBaseExVat,
                PanicButtonAddonExVat = _settings.PackagePricing.PanicButtonAddonExVat,
                AppLiveTrackingAddonExVat = _settings.PackagePricing.AppLiveTrackingAddonExVat,
                IsVatExempt = false
            },
            new()
            {
                Code = "STING-PLUS",
                Name = "STING Plus",
                Description = "STING Plus package",
                BasePriceExVat = _settings.PackagePricing.StingPlusBaseExVat,
                PanicButtonAddonExVat = _settings.PackagePricing.PanicButtonAddonExVat,
                AppLiveTrackingAddonExVat = _settings.PackagePricing.AppLiveTrackingAddonExVat,
                IsVatExempt = false
            },
            new()
            {
                Code = "STING-FM",
                Name = "STING FM",
                Description = "STING FM package",
                BasePriceExVat = _settings.PackagePricing.StingFmBaseExVat,
                PanicButtonAddonExVat = _settings.PackagePricing.PanicButtonAddonExVat,
                AppLiveTrackingAddonExVat = _settings.PackagePricing.AppLiveTrackingAddonExVat,
                IsVatExempt = false
            },
            new()
            {
                Code = "PANIC-BUTTON",
                Name = "Panic Button",
                Description = "Panic button add-on",
                BasePriceExVat = _settings.PackagePricing.PanicButtonAddonExVat,
                PanicButtonAddonExVat = 0m,
                AppLiveTrackingAddonExVat = 0m,
                IsVatExempt = false
            },
            new()
            {
                Code = "APP-LIVE-TRACKING",
                Name = "App Live Tracking",
                Description = "App live tracking add-on",
                BasePriceExVat = _settings.PackagePricing.AppLiveTrackingAddonExVat,
                PanicButtonAddonExVat = 0m,
                AppLiveTrackingAddonExVat = 0m,
                IsVatExempt = true
            }
        };
    }

    private bool EnsureRequiredProducts(List<ProductCatalogItem> items)
    {
        var updated = false;

        if (FindByCode(items, "PANIC-BUTTON") == null)
        {
            items.Add(new ProductCatalogItem
            {
                Code = "PANIC-BUTTON",
                Name = "Panic Button",
                Description = "Panic button add-on",
                BasePriceExVat = _settings.PackagePricing.PanicButtonAddonExVat,
                PanicButtonAddonExVat = 0m,
                AppLiveTrackingAddonExVat = 0m,
                IsVatExempt = false
            });
            updated = true;
        }

        if (FindByCode(items, "APP-LIVE-TRACKING") == null)
        {
            items.Add(new ProductCatalogItem
            {
                Code = "APP-LIVE-TRACKING",
                Name = "App Live Tracking",
                Description = "App live tracking add-on",
                BasePriceExVat = _settings.PackagePricing.AppLiveTrackingAddonExVat,
                PanicButtonAddonExVat = 0m,
                AppLiveTrackingAddonExVat = 0m,
                IsVatExempt = true
            });
            updated = true;
        }
        else
        {
            var appLive = FindByCode(items, "APP-LIVE-TRACKING");
            if (appLive != null && !appLive.IsVatExempt)
            {
                appLive.IsVatExempt = true;
                updated = true;
            }
        }

        return updated;
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();
}
