using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Models;
using StingListManager.Services;

namespace StingListManager.ViewModels
{
    public partial class QuoteLineItemRow : ObservableObject
    {
        private Action<QuoteLineItemRow>? _onChanged;

        [ObservableProperty]
        private int id;

        [ObservableProperty]
        private int lineNumber;

        [ObservableProperty]
        private string productName = "";

        [ObservableProperty]
        private string? productCode;

        [ObservableProperty]
        private ProductCatalogItem? selectedProduct;

        [ObservableProperty]
        private int quantity = 1;

        [ObservableProperty]
        private decimal unitPriceExVat;

        [ObservableProperty]
        private decimal lineTotalExVat;

        [ObservableProperty]
        private bool isVatExempt;

        [ObservableProperty]
        private string description = "";

        public void SetChangeHandler(Action<QuoteLineItemRow> onChanged)
        {
            _onChanged = onChanged;
        }

        partial void OnProductNameChanged(string value)
        {
            _onChanged?.Invoke(this);
        }

        partial void OnProductCodeChanged(string? value)
        {
            _onChanged?.Invoke(this);
        }

        partial void OnSelectedProductChanged(ProductCatalogItem? value)
        {
            if (value != null)
            {
                ProductCode = value.Code;
                ProductName = value.Name;
            }
            _onChanged?.Invoke(this);
        }

        partial void OnQuantityChanged(int value)
        {
            _onChanged?.Invoke(this);
        }

        partial void OnUnitPriceExVatChanged(decimal value)
        {
            _onChanged?.Invoke(this);
        }
    }

    public partial class QuoteEditViewModel : ViewModelBase
    {
        private const string AutoRemovalFeeCode = "AUTO-REMOVAL-FEE";
        private const string AutoRemovalFeeDescription = "Auto-added removal fee";
        private const string AutoInspectionFeeCode = WorkflowService.InspectionFeeCode;
        private const string AutoInspectionFeeDescription = "Auto-added inspection fee";
        private const string AutoMonthlyStingCode = "AUTO-MONTHLY-STING";
        private const string AutoMonthlyStingPlusCode = "AUTO-MONTHLY-STING-PLUS";
        private const string AutoMonthlyStingFmCode = "AUTO-MONTHLY-STING-FM";
        private bool _isApplyingAutomaticLineItems;
        private readonly HashSet<QuoteLineItemRow> _lineItemRecalcGuard = new();
        private readonly Action _close;
        private readonly int? _quoteId;
        private readonly AppState _appState;
        private readonly QuotePricingService _pricingService;
        private readonly ProductCatalogService _catalogService;

        [ObservableProperty]
        private int typeIndex;

        [ObservableProperty]
        private string company = "";

        [ObservableProperty]
        private string registration = "";

        [ObservableProperty]
        private string fleetNumber = "";

        [ObservableProperty]
        private string notes = "";

        [ObservableProperty]
        private string errorMessage = "";

        [ObservableProperty]
        private decimal subtotalExVat;

        [ObservableProperty]
        private decimal vatAmount;

        [ObservableProperty]
        private decimal totalIncVat;

        [ObservableProperty]
        private QuoteLineItemRow? selectedLineItem;

        public ObservableCollection<QuoteLineItemRow> LineItems { get; } = new();
        public ObservableCollection<ProductCatalogItem> Products { get; } = new();
        public ObservableCollection<string> ClientNames { get; } = new();

        public bool IsRemovalQuote => TypeIndex == 1;
        public bool IsInspectionQuote => TypeIndex == 2;

        partial void OnTypeIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsRemovalQuote));
            OnPropertyChanged(nameof(IsInspectionQuote));
            EnsureAutomaticLineItems();
        }

        public QuoteEditViewModel(Action close, int? quoteId, AppState appState, QuotePricingService pricingService)
        {
            _close = close;
            _quoteId = quoteId;
            _appState = appState;
            _pricingService = pricingService;
            _catalogService = new ProductCatalogService(_appState.Settings);

            LoadClients();

            foreach (var product in _catalogService.LoadCatalog())
            {
                Products.Add(product);
            }

            if (quoteId is not null)
            {
                LoadQuote(quoteId.Value);
            }
            else
            {
                EnsureAutomaticLineItems();
            }
        }

        private void LoadClients()
        {
            ClientNames.Clear();
            using var db = new AppDbContext();
            foreach (var name in db.Clients.AsNoTracking()
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList())
            {
                ClientNames.Add(name);
            }
        }

        private void LoadQuote(int quoteId)
        {
            try
            {
                using var db = new AppDbContext();
                var quote = db.Quotes
                    .Include(q => q.LineItems)
                    .FirstOrDefault(q => q.Id == quoteId);

                if (quote is null)
                    return;

                TypeIndex = quote.Type switch
                {
                    QuoteType.Removal => 1,
                    QuoteType.Inspection => 2,
                    _ => 0
                };
                Company = quote.Company ?? "";
                Registration = quote.Registration ?? "";
                FleetNumber = quote.FleetNumber ?? "";
                Notes = quote.Notes ?? "";

                LineItems.Clear();
                foreach (var item in quote.LineItems.OrderBy(x => x.LineNumber))
                {
                    var matchedProduct = _catalogService.FindByCode(Products, item.ProductCode) 
                        ?? _catalogService.FindByName(Products, item.ProductName ?? item.ProductType);

                    var row = new QuoteLineItemRow
                    {
                        Id = item.Id,
                        LineNumber = item.LineNumber,
                        ProductName = item.ProductName ?? item.ProductType ?? "",
                        ProductCode = item.ProductCode,
                        SelectedProduct = matchedProduct,
                        Quantity = item.Quantity,
                        UnitPriceExVat = item.UnitPriceExVat,
                        LineTotalExVat = item.LineTotalExVat,
                        IsVatExempt = item.IsVatExempt,
                        Description = item.Description ?? ""
                    };
                    row.SetChangeHandler(RecalculateLineItemTotal);
                    LineItems.Add(row);
                }

                EnsureAutomaticLineItems();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading quote: {ex.Message}";
            }
        }





        [RelayCommand]
        private void AddLineItem()
        {
            var lineNumber = (LineItems.Count > 0 ? LineItems.Max(x => x.LineNumber) : 0) + 1;
            var row = new QuoteLineItemRow { LineNumber = lineNumber };
            row.SetChangeHandler(RecalculateLineItemTotal);
            LineItems.Add(row);
            EnsureAutomaticLineItems();
        }

        [RelayCommand]
        private void RemoveLineItem(QuoteLineItemRow? item)
        {
            if (item is null) return;
            LineItems.Remove(item);
            EnsureAutomaticLineItems();
        }

        public void RecalculateTotals()
        {
            SubtotalExVat = LineItems.Sum(x => x.LineTotalExVat);
            var vatBase = LineItems.Where(x => !x.IsVatExempt).Sum(x => x.LineTotalExVat);
            VatAmount = vatBase * _appState.Settings.VatRate;
            TotalIncVat = SubtotalExVat + VatAmount;
        }

        public void RecalculateLineItemTotal(QuoteLineItemRow item)
        {
            if (_isApplyingAutomaticLineItems)
            {
                var hasAutoDescription = string.Equals(item.Description, AutoRemovalFeeDescription, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Description, AutoInspectionFeeDescription, StringComparison.OrdinalIgnoreCase);
                var isAutoFeeCode = string.Equals(item.ProductCode, AutoRemovalFeeCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ProductCode, AutoInspectionFeeCode, StringComparison.OrdinalIgnoreCase);
                if (!isAutoFeeCode && hasAutoDescription)
                {
                    item.Description = string.Empty;
                }

                item.LineTotalExVat = item.UnitPriceExVat * item.Quantity;
                return;
            }

            // Protect against recursive change notifications while normalizing row values.
            if (!_lineItemRecalcGuard.Add(item))
            {
                return;
            }

            try
            {
                var matchedProduct = _catalogService.FindByCode(Products, item.ProductCode)
                    ?? _catalogService.FindByName(Products, item.ProductName);

                if (matchedProduct != null)
                {
                    item.ProductCode = matchedProduct.Code;
                    item.ProductName = matchedProduct.Name;
                    item.UnitPriceExVat = matchedProduct.BasePriceExVat;
                    item.IsVatExempt = matchedProduct.IsVatExempt;
                }
                else
                {
                    var tempQuote = new Quote
                    {
                        ProductType = item.ProductName,
                        AmountExVat = 0
                    };
                    var calculated = _pricingService.CalculateExVatAmount(tempQuote);
                    if (calculated > 0)
                    {
                        item.UnitPriceExVat = calculated;
                    }
                    item.IsVatExempt = false;
                }

                // If an auto fee row is converted to another product (e.g. App Live Tracking),
                // clear the auto marker so it is no longer treated as an auto row.
                var hasAutoDescription = string.Equals(item.Description, AutoRemovalFeeDescription, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Description, AutoInspectionFeeDescription, StringComparison.OrdinalIgnoreCase);
                var isAutoFeeCode = string.Equals(item.ProductCode, AutoRemovalFeeCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ProductCode, AutoInspectionFeeCode, StringComparison.OrdinalIgnoreCase);
                if (!isAutoFeeCode && hasAutoDescription)
                {
                    item.Description = string.Empty;
                }

                item.LineTotalExVat = item.UnitPriceExVat * item.Quantity;
                EnsureAutomaticLineItems();
                RecalculateTotals();
            }
            finally
            {
                _lineItemRecalcGuard.Remove(item);
            }
        }

        [RelayCommand]
        private void Save()
        {
            ErrorMessage = null;
            EnsureAutomaticLineItems();

            if (string.IsNullOrWhiteSpace(Company))
            {
                ErrorMessage = "Company is required.";
                return;
            }

            if (IsRemovalQuote)
            {
                if (string.IsNullOrWhiteSpace(Registration))
                {
                    ErrorMessage = "Registration is required for removal quotes.";
                    return;
                }
            }
            else if (IsInspectionQuote && string.IsNullOrWhiteSpace(Registration))
            {
                ErrorMessage = "Registration is required for inspection quotes.";
                return;
            }

            if (LineItems.Count == 0 && !IsRemovalQuote)
            {
                ErrorMessage = "At least one line item is required.";
                return;
            }

            try
            {
                using var db = new AppDbContext();

                Quote q;
                if (_quoteId is null)
                {
                    q = new Quote { CreatedAt = DateTime.UtcNow };
                    q.QuoteNumber = QuoteNumberAllocator.GetNext(db);
                    db.Quotes.Add(q);
                }
                else
                {
                    q = db.Quotes.Include("LineItems").First(x => x.Id == _quoteId.Value);
                    q.LineItems.Clear();
                }

                q.Type = TypeIndex switch
                {
                    1 => QuoteType.Removal,
                    2 => QuoteType.Inspection,
                    _ => QuoteType.Install
                };
                q.Company = Company.Trim();
                q.Registration = string.IsNullOrWhiteSpace(Registration) ? null : Registration.Trim().ToUpperInvariant();
                q.FleetNumber = string.IsNullOrWhiteSpace(FleetNumber) ? null : FleetNumber.Trim();
                q.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
                q.AmountExVat = SubtotalExVat;

                foreach (var row in LineItems.ToList())
                {
                    RecalculateLineItemTotal(row);
                }

                foreach (var row in LineItems)
                {
                    q.LineItems.Add(new QuoteLineItem
                    {
                        LineNumber = row.LineNumber,
                        ProductType = row.ProductName,
                        ProductCode = row.ProductCode,
                        ProductName = row.ProductName,
                        Quantity = row.Quantity,
                        IncludesPanicButton = false,
                        IncludesAppLiveTracking = false,
                        UnitPriceExVat = row.UnitPriceExVat,
                        LineTotalExVat = row.LineTotalExVat,
                        IsVatExempt = row.IsVatExempt,
                        Description = row.Description
                    });
                }

                db.SaveChanges();
                var quoteReference = QuoteReferenceFormatter.Format(q.QuoteNumber);
                _appState.SetStatus(_quoteId is null
                    ? $"Quote {quoteReference} created."
                    : $"Quote {quoteReference} updated.");
                _close();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error saving quote: {ex.Message}";
            }
        }

        private enum UnitFamily
        {
            None = 0,
            Sting = 1,
            StingPlus = 2,
            StingFm = 3
        }

        private void EnsureAutomaticLineItems()
        {
            if (_isApplyingAutomaticLineItems)
                return;

            _isApplyingAutomaticLineItems = true;
            try
            {
                if (IsRemovalQuote)
                {
                    RemoveAutoInspectionFeeLines();
                    RemoveAutoMonthlyLines();
                    if (ShouldAutoAddRemovalFee())
                    {
                        EnsureRemovalFeeLine();
                    }
                    else
                    {
                        RemoveAutoRemovalFeeLines();
                    }
                }
                else if (IsInspectionQuote)
                {
                    RemoveAutoRemovalFeeLines();
                    RemoveAutoMonthlyLines();
                    if (ShouldAutoAddInspectionFee())
                    {
                        EnsureInspectionFeeLine();
                    }
                    else
                    {
                        RemoveAutoInspectionFeeLines();
                    }
                }
                else
                {
                    RemoveAutoRemovalFeeLines();
                    RemoveAutoInspectionFeeLines();
                    EnsureMonthlyFeeLines();
                }

                RenumberLineItems();
                foreach (var row in LineItems)
                {
                    row.LineTotalExVat = row.UnitPriceExVat * row.Quantity;
                }
            }
            finally
            {
                _isApplyingAutomaticLineItems = false;
            }

            RecalculateTotals();
        }

        private bool ShouldAutoAddRemovalFee()
        {
            var manualRows = LineItems.Where(x => !IsAutoRow(x)).ToList();
            if (manualRows.Count == 0)
                return true;

            // Live-tracking-only removals should not auto-add removal fee.
            var hasNonLiveTrackingManualLine = manualRows.Any(x => !IsLiveTrackingLine(x));
            return hasNonLiveTrackingManualLine;
        }

        private bool ShouldAutoAddInspectionFee()
        {
            var manualRows = LineItems.Where(x => !IsAutoRow(x)).ToList();
            if (manualRows.Count == 0)
                return true;

            return !manualRows.Any(IsUnitLine);
        }

        private void EnsureRemovalFeeLine()
        {
            var autoRows = LineItems.Where(IsAnyRemovalFeeLine).ToList();
            foreach (var extra in autoRows.Skip(1))
            {
                LineItems.Remove(extra);
            }

            var row = autoRows.FirstOrDefault();
            if (row == null)
            {
                row = new QuoteLineItemRow();
                row.SetChangeHandler(RecalculateLineItemTotal);
                LineItems.Add(row);
            }

            row.SelectedProduct = null;
            row.ProductCode = AutoRemovalFeeCode;
            row.ProductName = "Removal Fee";
            row.Quantity = 1;
            row.UnitPriceExVat = _appState.Settings.DefaultRemovalFeeExVat;
            row.IsVatExempt = false;
            row.Description = AutoRemovalFeeDescription;
            row.LineTotalExVat = row.UnitPriceExVat * row.Quantity;
        }

        private void EnsureInspectionFeeLine()
        {
            var autoRows = LineItems.Where(IsAnyInspectionFeeLine).ToList();
            foreach (var extra in autoRows.Skip(1))
            {
                LineItems.Remove(extra);
            }

            var row = autoRows.FirstOrDefault();
            if (row == null)
            {
                row = new QuoteLineItemRow();
                row.SetChangeHandler(RecalculateLineItemTotal);
                LineItems.Add(row);
            }

            row.SelectedProduct = null;
            row.ProductCode = AutoInspectionFeeCode;
            row.ProductName = "Inspection Fee";
            row.Quantity = 1;
            row.UnitPriceExVat = _appState.Settings.DefaultInspectionFeeExVat;
            row.IsVatExempt = false;
            row.Description = AutoInspectionFeeDescription;
            row.LineTotalExVat = row.UnitPriceExVat * row.Quantity;
        }

        private void EnsureMonthlyFeeLines()
        {
            var qtyByFamily = new Dictionary<UnitFamily, int>
            {
                { UnitFamily.Sting, 0 },
                { UnitFamily.StingPlus, 0 },
                { UnitFamily.StingFm, 0 }
            };

            foreach (var row in LineItems.Where(x => !IsAutoRow(x)))
            {
                var family = GetUnitFamily(row);
                if (family == UnitFamily.None)
                    continue;

                qtyByFamily[family] += row.Quantity <= 0 ? 1 : row.Quantity;
            }

            SyncMonthlyLine(UnitFamily.Sting, qtyByFamily[UnitFamily.Sting], AutoMonthlyStingCode, "STING Monthly Fee", 150m);
            SyncMonthlyLine(UnitFamily.StingPlus, qtyByFamily[UnitFamily.StingPlus], AutoMonthlyStingPlusCode, "STING PLUS Monthly Fee", 180m);
            SyncMonthlyLine(UnitFamily.StingFm, qtyByFamily[UnitFamily.StingFm], AutoMonthlyStingFmCode, "STING FM Monthly Fee", 235m);
        }

        private void SyncMonthlyLine(UnitFamily family, int quantity, string code, string name, decimal unitPrice)
        {
            var existing = LineItems.FirstOrDefault(x => IsAutoMonthlyLine(x) && string.Equals(x.ProductCode, code, StringComparison.OrdinalIgnoreCase));

            if (quantity <= 0)
            {
                if (existing != null)
                    LineItems.Remove(existing);
                return;
            }

            if (existing == null)
            {
                existing = new QuoteLineItemRow();
                existing.SetChangeHandler(RecalculateLineItemTotal);
                LineItems.Add(existing);
            }

            existing.SelectedProduct = null;
            existing.ProductCode = code;
            existing.ProductName = name;
            existing.Quantity = quantity;
            existing.UnitPriceExVat = unitPrice;
            existing.IsVatExempt = false;
            existing.Description = "Monthly fee";
            existing.LineTotalExVat = existing.UnitPriceExVat * existing.Quantity;
        }

        private void RemoveAutoMonthlyLines()
        {
            foreach (var row in LineItems.Where(IsAutoMonthlyLine).ToList())
            {
                LineItems.Remove(row);
            }
        }

        private void RemoveAutoRemovalFeeLines()
        {
            foreach (var row in LineItems.Where(IsAutoRemovalFeeLine).ToList())
            {
                LineItems.Remove(row);
            }
        }

        private void RemoveAutoInspectionFeeLines()
        {
            foreach (var row in LineItems.Where(IsAutoInspectionFeeLine).ToList())
            {
                LineItems.Remove(row);
            }
        }

        private void RenumberLineItems()
        {
            var n = 1;
            foreach (var row in LineItems)
            {
                row.LineNumber = n++;
            }
        }

        private static bool IsAutoRemovalFeeLine(QuoteLineItemRow row)
        {
            if (string.Equals(row.ProductCode, AutoRemovalFeeCode, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(row.Description, AutoRemovalFeeDescription, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(row.Description, "Auto-added removal fee", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAnyRemovalFeeLine(QuoteLineItemRow row)
        {
            if (IsAutoRemovalFeeLine(row))
                return true;

            if (string.Equals(row.ProductCode, "REMOVAL-FEE", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(row.ProductName, "Removal Fee", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(row.ProductName, "Removal fee", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoInspectionFeeLine(QuoteLineItemRow row)
        {
            if (string.Equals(row.ProductCode, AutoInspectionFeeCode, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(row.Description, AutoInspectionFeeDescription, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(row.Description, "Auto-added inspection fee", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAnyInspectionFeeLine(QuoteLineItemRow row)
        {
            if (IsAutoInspectionFeeLine(row))
                return true;

            if (string.Equals(row.ProductCode, "INSPECTION-FEE", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(row.ProductName, "Inspection Fee", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(row.ProductName, "Inspection fee", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLiveTrackingLine(QuoteLineItemRow row)
        {
            if (string.Equals(row.ProductCode, "APP-LIVE-TRACKING", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(row.ProductName)
                && row.ProductName.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(row.ProductCode)
                && row.ProductCode.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsAutoMonthlyLine(QuoteLineItemRow row)
        {
            return (row.ProductCode ?? string.Empty).StartsWith("AUTO-MONTHLY-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoRow(QuoteLineItemRow row)
        {
            return IsAutoRemovalFeeLine(row) || IsAutoInspectionFeeLine(row) || IsAutoMonthlyLine(row);
        }

        private static bool IsUnitLine(QuoteLineItemRow row)
        {
            return GetUnitFamily(row) != UnitFamily.None;
        }

        private static UnitFamily GetUnitFamily(QuoteLineItemRow row)
        {
            var text = $"{row.ProductCode} {row.ProductName}".Trim();
            if (string.IsNullOrWhiteSpace(text))
                return UnitFamily.None;

            var value = text.ToUpperInvariant();
            if (value.Contains("STING FM", StringComparison.Ordinal) || value.Contains("STING-FM", StringComparison.Ordinal))
                return UnitFamily.StingFm;

            if (value.Contains("STING PLUS", StringComparison.Ordinal)
                || value.Contains("STING+", StringComparison.Ordinal)
                || value.Contains("STING-PLUS", StringComparison.Ordinal))
                return UnitFamily.StingPlus;

            if (value.Contains("STING", StringComparison.Ordinal))
                return UnitFamily.Sting;

            return UnitFamily.None;
        }
    }
}
