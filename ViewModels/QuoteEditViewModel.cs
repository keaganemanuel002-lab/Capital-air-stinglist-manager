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

        [ObservableProperty]
        private Client? selectedClient;

        public ObservableCollection<QuoteLineItemRow> LineItems { get; } = new();
        public ObservableCollection<ProductCatalogItem> Products { get; } = new();
        public ObservableCollection<Client> Clients { get; } = new();

        public bool IsRemovalQuote => TypeIndex == 1;

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
        }

        private void LoadClients()
        {
            Clients.Clear();
            using var db = new AppDbContext();
            foreach (var client in db.Clients.AsNoTracking().OrderBy(c => c.Name).ToList())
            {
                Clients.Add(client);
            }
        }

        partial void OnSelectedClientChanged(Client? value)
        {
            if (value != null)
            {
                Company = value.Name;
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

                TypeIndex = quote.Type == QuoteType.Removal ? 1 : 0;
                Company = quote.Company ?? "";
                SelectedClient = Clients.FirstOrDefault(c => string.Equals(c.Name, Company, StringComparison.OrdinalIgnoreCase));
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

                RecalculateTotals();
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
        }

        [RelayCommand]
        private void RemoveLineItem(QuoteLineItemRow? item)
        {
            if (item is null) return;
            LineItems.Remove(item);
            RecalculateTotals();
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
            item.LineTotalExVat = item.UnitPriceExVat * item.Quantity;
            RecalculateTotals();
        }

        [RelayCommand]
        private void Save()
        {
            ErrorMessage = null;

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
                    var maxQuoteNumber = db.Quotes.Any() ? db.Quotes.Max(x => x.QuoteNumber) : 0;
                    q.QuoteNumber = maxQuoteNumber + 1;
                    db.Quotes.Add(q);
                }
                else
                {
                    q = db.Quotes.Include("LineItems").First(x => x.Id == _quoteId.Value);
                    q.LineItems.Clear();
                }

                q.Type = TypeIndex == 0 ? QuoteType.Install : QuoteType.Removal;
                q.Company = Company.Trim();
                q.Registration = string.IsNullOrWhiteSpace(Registration) ? null : Registration.Trim().ToUpperInvariant();
                q.FleetNumber = string.IsNullOrWhiteSpace(FleetNumber) ? null : FleetNumber.Trim();
                q.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
                q.AmountExVat = SubtotalExVat;

                foreach (var row in LineItems)
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
                _close();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error saving quote: {ex.Message}";
            }
        }
    }
}
