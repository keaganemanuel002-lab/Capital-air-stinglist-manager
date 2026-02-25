using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Views;

namespace StingListManager.ViewModels;

public sealed class PurchaseOrderRow
{
    public int Id { get; init; }
    public int OrderNumber { get; init; }
    public string OrderReference { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public string Supplier { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal AmountExVat { get; init; }
    public decimal VatAmount { get; init; }
    public decimal TotalAmountIncVat { get; init; }
    public string Status { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public sealed class SupplierOption
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NameNorm { get; init; } = string.Empty;
    public bool QuoteIncludesVatDefault { get; init; }

    public override string ToString()
    {
        return Name;
    }
}

public partial class PurchaseOrderLineItemRow : ObservableObject
{
    private readonly Action _onChanged;

    public PurchaseOrderLineItemRow(Action onChanged)
    {
        _onChanged = onChanged;
    }

    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private decimal quantity = 1m;
    [ObservableProperty] private decimal unitPrice;
    [ObservableProperty] private decimal amountExVat;
    [ObservableProperty] private decimal vatAmount;
    [ObservableProperty] private decimal totalAmountIncVat;

    partial void OnDescriptionChanged(string value) => _onChanged();
    partial void OnQuantityChanged(decimal value) => _onChanged();
    partial void OnUnitPriceChanged(decimal value) => _onChanged();
}

public partial class PurchaseOrdersViewModel : ViewModelBase
{
    private const int MaxLineItems = 10;

    private readonly PurchaseOrdersWindow _window;
    private readonly string _signedInUser;
    private readonly string _signedInRole;
    private readonly List<PurchaseOrderRow> _allRows = new();
    private bool _suppressLineRecalculate;
    private int? _editingId;

    public ObservableCollection<PurchaseOrderRow> Rows { get; } = new();
    public ObservableCollection<SupplierOption> SupplierOptions { get; } = new();
    public ObservableCollection<PurchaseOrderLineItemRow> LineItems { get; } = new();
    public List<string> StatusOptions { get; } = new()
    {
        "Draft",
        "Submitted",
        "Approved",
        "Ordered",
        "Received",
        "Cancelled"
    };

    [ObservableProperty] private PurchaseOrderRow? selectedRow;
    [ObservableProperty] private PurchaseOrderLineItemRow? selectedLineItem;
    [ObservableProperty] private SupplierOption? selectedSupplier;
    [ObservableProperty] private string searchText = string.Empty;

    [ObservableProperty] private int orderNumber;
    [ObservableProperty] private string supplier = string.Empty;
    [ObservableProperty] private bool quoteIncludesVat;
    [ObservableProperty] private decimal vatRatePercent = 15m;
    [ObservableProperty] private string selectedStatus = "Draft";
    [ObservableProperty] private string requestedBy = string.Empty;
    [ObservableProperty] private DateTimeOffset? orderDate = DateTimeOffset.Now.Date;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private decimal amountExVat;
    [ObservableProperty] private decimal vatAmount;
    [ObservableProperty] private decimal totalAmountIncVat;
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private bool statusIsError;

    public string StatusColor => StatusIsError ? "#B91C1C" : "#334155";
    public string SignedInAs => $"{_signedInUser} ({_signedInRole})";
    public string OrderReference => FormatOrderReference(OrderNumber);
    public string LineItemsCountLabel => $"{LineItems.Count}/{MaxLineItems} items";
    public string LineItemsVatHint => QuoteIncludesVat
        ? "Line prices are VAT-inclusive."
        : "Line prices are VAT-exclusive.";

    public PurchaseOrdersViewModel(PurchaseOrdersWindow window, string signedInUser, string signedInRole)
    {
        _window = window;
        _signedInUser = string.IsNullOrWhiteSpace(signedInUser) ? Environment.UserName : signedInUser.Trim();
        _signedInRole = string.IsNullOrWhiteSpace(signedInRole) ? "Tech" : signedInRole.Trim();

        LoadSupplierOptions();
        LoadRows();
        PrepareNewOrder();
    }

    partial void OnSelectedRowChanged(PurchaseOrderRow? value)
    {
        if (value is null)
            return;

        LoadOrderIntoEditor(value.Id);
    }

    partial void OnSelectedSupplierChanged(SupplierOption? value)
    {
        if (value is null)
            return;

        Supplier = value.Name;
        QuoteIncludesVat = value.QuoteIncludesVatDefault;
    }

    partial void OnVatRatePercentChanged(decimal value)
    {
        RecalculateLineTotals();
    }

    partial void OnQuoteIncludesVatChanged(bool value)
    {
        OnPropertyChanged(nameof(LineItemsVatHint));
        RecalculateLineTotals();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnOrderNumberChanged(int value)
    {
        OnPropertyChanged(nameof(OrderReference));
    }

    partial void OnStatusIsErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadSupplierOptions();
        LoadRows();
        SetStatus($"Loaded {_allRows.Count} purchase order(s).");
    }

    [RelayCommand]
    private void NewOrder()
    {
        PrepareNewOrder();
    }

    [RelayCommand]
    private void AddLineItem()
    {
        if (LineItems.Count >= MaxLineItems)
        {
            SetStatus($"Maximum {MaxLineItems} line items allowed.", true);
            return;
        }

        AddLineItemInternal(description: string.Empty, quantity: 1m, unitPrice: 0m);
        SetStatus($"Line item added ({LineItems.Count}/{MaxLineItems}).");
    }

    [RelayCommand]
    private void RemoveSelectedLineItem()
    {
        if (SelectedLineItem is null)
        {
            SetStatus("Select a line item to remove.", true);
            return;
        }

        LineItems.Remove(SelectedLineItem);
        ReindexLineItems();

        if (LineItems.Count == 0)
            AddLineItemInternal(description: string.Empty, quantity: 1m, unitPrice: 0m);

        SetStatus("Line item removed.");
    }

    [RelayCommand]
    private void SaveSupplierToList()
    {
        var normalizedSupplier = NormalizeSingleLine(Supplier);
        if (string.IsNullOrWhiteSpace(normalizedSupplier))
        {
            SetStatus("Supplier name is required.", true);
            return;
        }

        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);
        UpsertSupplier(db, normalizedSupplier, QuoteIncludesVat);
        db.SaveChanges();

        LoadSupplierOptions(normalizedSupplier);
        SetStatus($"Supplier '{normalizedSupplier}' saved to supplier list.");
    }

    [RelayCommand]
    private void SaveOrder()
    {
        var normalizedSupplier = NormalizeSingleLine(Supplier);
        if (string.IsNullOrWhiteSpace(normalizedSupplier))
        {
            SetStatus("Supplier is required.", true);
            return;
        }

        var normalizedRequestedBy = NormalizeSingleLine(RequestedBy);
        if (string.IsNullOrWhiteSpace(normalizedRequestedBy))
            normalizedRequestedBy = _signedInUser;

        var normalizedStatus = string.IsNullOrWhiteSpace(SelectedStatus)
            ? "Draft"
            : SelectedStatus.Trim();

        var preparedItems = BuildPreparedLineItems();
        if (preparedItems.Count == 0)
        {
            SetStatus("Add at least one line item with description and amount.", true);
            return;
        }

        if (preparedItems.Count > MaxLineItems)
        {
            SetStatus($"Maximum {MaxLineItems} line items allowed.", true);
            return;
        }

        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);
        UpsertSupplier(db, normalizedSupplier, QuoteIncludesVat);

        PurchaseOrder entity;
        var isNew = _editingId is null;
        if (isNew)
        {
            entity = new PurchaseOrder
            {
                CreatedAt = DateTime.UtcNow,
                OrderNumber = GetNextOrderNumber(db)
            };
            db.PurchaseOrders.Add(entity);
        }
        else
        {
            entity = db.PurchaseOrders
                .Include(x => x.LineItems)
                .FirstOrDefault(x => x.Id == _editingId!.Value)
                ?? new PurchaseOrder();

            if (entity.Id == 0)
            {
                SetStatus("Selected purchase order no longer exists.", true);
                return;
            }
        }

        entity.Supplier = normalizedSupplier;
        entity.Description = BuildOrderSummary(preparedItems);
        entity.QuoteIncludesVat = QuoteIncludesVat;
        entity.VatRate = Math.Round(Math.Max(0m, VatRatePercent) / 100m, 4);
        entity.AmountExVat = Math.Round(preparedItems.Sum(x => x.AmountExVat), 2);
        entity.VatAmount = Math.Round(preparedItems.Sum(x => x.VatAmount), 2);
        entity.TotalAmountIncVat = Math.Round(preparedItems.Sum(x => x.TotalAmountIncVat), 2);
        entity.Status = normalizedStatus;
        entity.RequestedBy = normalizedRequestedBy;
        entity.OrderDate = ToUtc(OrderDate);
        entity.Notes = TrimOrNull(Notes);
        entity.UpdatedAt = DateTime.UtcNow;

        db.SaveChanges();

        var existingItems = db.PurchaseOrderLineItems
            .Where(x => x.PurchaseOrderId == entity.Id)
            .ToList();
        if (existingItems.Count > 0)
            db.PurchaseOrderLineItems.RemoveRange(existingItems);

        foreach (var item in preparedItems)
        {
            db.PurchaseOrderLineItems.Add(new PurchaseOrderLineItem
            {
                PurchaseOrderId = entity.Id,
                LineNumber = item.LineNumber,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                AmountExVat = item.AmountExVat,
                VatAmount = item.VatAmount,
                TotalAmountIncVat = item.TotalAmountIncVat
            });
        }

        db.SaveChanges();

        var reference = FormatOrderReference(entity.OrderNumber);
        LoadSupplierOptions(normalizedSupplier);
        LoadRows();

        if (isNew)
        {
            PrepareNewOrder(skipReadyMessage: true);
            SetStatus($"Purchase order {reference} created.");
            return;
        }

        SelectRow(entity.Id);
        SetStatus($"Purchase order {reference} updated.");
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRow is null)
        {
            SetStatus("Select a purchase order first.", true);
            return;
        }

        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);

        var entity = db.PurchaseOrders.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (entity is null)
        {
            SetStatus("Selected purchase order no longer exists.", true);
            return;
        }

        var reference = FormatOrderReference(entity.OrderNumber);
        db.PurchaseOrders.Remove(entity);
        db.SaveChanges();

        LoadRows();
        PrepareNewOrder(skipReadyMessage: true);
        SetStatus($"Purchase order {reference} deleted.");
    }

    [RelayCommand]
    private void BackToMenu()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var launcher = new WorkspaceLauncherWindow(_signedInUser, _signedInRole);
        _window.Hide();

        launcher.Closed += (_, _) =>
        {
            if (launcher.SelectedWorkspace is null
                || launcher.SelectedWorkspace == WorkspaceChoice.Orders)
            {
                desktop.MainWindow = _window;
                _window.Show();
                return;
            }

            var mainWindow = new MainWindow(_signedInUser, _signedInRole);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            _window.Close();
        };

        desktop.MainWindow = launcher;
        launcher.Show();
    }

    [RelayCommand]
    private void Logout()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var loginWindow = new LoginWindow();
        _window.Hide();

        loginWindow.Closed += (_, _) =>
        {
            if (!loginWindow.LoginSucceeded)
            {
                desktop.Shutdown();
                return;
            }

            var launcher = new WorkspaceLauncherWindow(
                loginWindow.AuthenticatedUsername,
                loginWindow.AuthenticatedRole);

            launcher.Closed += (_, _) =>
            {
                if (launcher.SelectedWorkspace is null)
                {
                    desktop.Shutdown();
                    return;
                }

                switch (launcher.SelectedWorkspace.Value)
                {
                    case WorkspaceChoice.Orders:
                    {
                        var ordersWindow = new PurchaseOrdersWindow(
                            loginWindow.AuthenticatedUsername,
                            loginWindow.AuthenticatedRole);
                        desktop.MainWindow = ordersWindow;
                        ordersWindow.Show();
                        break;
                    }
                    case WorkspaceChoice.StingManager:
                    default:
                    {
                        var mainWindow = new MainWindow(
                            loginWindow.AuthenticatedUsername,
                            loginWindow.AuthenticatedRole);
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                        break;
                    }
                }
            };

            desktop.MainWindow = launcher;
            launcher.Show();
            _window.Close();
        };

        desktop.MainWindow = loginWindow;
        loginWindow.Show();
    }

    private void LoadRows()
    {
        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);

        var rows = db.PurchaseOrders
            .AsNoTracking()
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.OrderNumber)
            .ToList()
            .Select(MapRow)
            .ToList();

        _allRows.Clear();
        _allRows.AddRange(rows);
        ApplyFilter();
    }

    private void LoadOrderIntoEditor(int orderId)
    {
        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);

        var entity = db.PurchaseOrders
            .Include(x => x.LineItems)
            .FirstOrDefault(x => x.Id == orderId);
        if (entity is null)
        {
            SetStatus("Selected purchase order no longer exists.", true);
            return;
        }

        _editingId = entity.Id;
        OrderNumber = entity.OrderNumber;
        Supplier = entity.Supplier;
        QuoteIncludesVat = entity.QuoteIncludesVat;
        VatRatePercent = Math.Round(entity.VatRate * 100m, 2);
        SelectedStatus = string.IsNullOrWhiteSpace(entity.Status) ? "Draft" : entity.Status;
        RequestedBy = string.IsNullOrWhiteSpace(entity.RequestedBy) ? _signedInUser : entity.RequestedBy;
        OrderDate = new DateTimeOffset(ToLocal(entity.OrderDate).Date);
        Notes = entity.Notes;

        var supplierNorm = NormalizeComparable(entity.Supplier);
        SelectedSupplier = SupplierOptions.FirstOrDefault(x => x.NameNorm == supplierNorm);

        ClearLineItems();

        var sourceItems = entity.LineItems
            .OrderBy(x => x.LineNumber)
            .ToList();
        if (sourceItems.Count == 0)
        {
            var legacyDescription = string.IsNullOrWhiteSpace(entity.Description)
                ? "Item 1"
                : entity.Description.Trim();

            var unitPrice = entity.QuoteIncludesVat
                ? entity.TotalAmountIncVat
                : entity.AmountExVat;

            AddLineItemInternal(legacyDescription, 1m, unitPrice);
        }
        else
        {
            foreach (var item in sourceItems)
            {
                AddLineItemInternal(item.Description, item.Quantity, item.UnitPrice);
            }
        }

        ReindexLineItems();
        RecalculateLineTotals();
        SetStatus($"Editing {FormatOrderReference(entity.OrderNumber)}.");
    }

    private void ApplyFilter()
    {
        IEnumerable<PurchaseOrderRow> query = _allRows;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(x =>
                ContainsIgnoreCase(x.OrderReference, search)
                || ContainsIgnoreCase(x.Supplier, search)
                || ContainsIgnoreCase(x.Description, search)
                || ContainsIgnoreCase(x.Status, search)
                || ContainsIgnoreCase(x.RequestedBy, search)
                || ContainsIgnoreCase(x.Notes, search));
        }

        var filtered = query.ToList();
        Rows.Clear();
        foreach (var row in filtered)
        {
            Rows.Add(row);
        }
    }

    private void LoadSupplierOptions(string? selectSupplierName = null)
    {
        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);

        var suppliers = db.PurchaseOrderSuppliers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToList();

        SupplierOptions.Clear();
        foreach (var supplier in suppliers)
        {
            SupplierOptions.Add(new SupplierOption
            {
                Id = supplier.Id,
                Name = supplier.Name,
                NameNorm = supplier.NameNorm,
                QuoteIncludesVatDefault = supplier.QuoteIncludesVatDefault
            });
        }

        var targetNorm = NormalizeComparable(selectSupplierName ?? Supplier);
        if (string.IsNullOrWhiteSpace(targetNorm))
            return;

        SelectedSupplier = SupplierOptions.FirstOrDefault(x => x.NameNorm == targetNorm);
    }

    private void PrepareNewOrder(bool skipReadyMessage = false)
    {
        using var db = new OrdersDbContext();
        OrdersDbContext.EnsureSchema(db);

        _editingId = null;
        SelectedRow = null;
        SelectedLineItem = null;
        SelectedSupplier = null;

        OrderNumber = GetNextOrderNumber(db);
        Supplier = string.Empty;
        QuoteIncludesVat = false;
        VatRatePercent = 15m;
        SelectedStatus = "Draft";
        RequestedBy = _signedInUser;
        OrderDate = DateTimeOffset.Now.Date;
        Notes = null;

        ClearLineItems();
        AddLineItemInternal(description: string.Empty, quantity: 1m, unitPrice: 0m);
        RecalculateLineTotals();

        if (!skipReadyMessage)
            SetStatus($"Ready to create {OrderReference}.");
    }

    private void AddLineItemInternal(string description, decimal quantity, decimal unitPrice)
    {
        var item = new PurchaseOrderLineItemRow(OnLineItemChanged)
        {
            LineNumber = LineItems.Count + 1,
            Description = description,
            Quantity = quantity <= 0 ? 1m : quantity,
            UnitPrice = Math.Max(0m, unitPrice)
        };

        LineItems.Add(item);
        OnPropertyChanged(nameof(LineItemsCountLabel));
        RecalculateLineTotals();
    }

    private void ReindexLineItems()
    {
        var index = 1;
        foreach (var item in LineItems)
        {
            item.LineNumber = index++;
        }

        OnPropertyChanged(nameof(LineItemsCountLabel));
        RecalculateLineTotals();
    }

    private void ClearLineItems()
    {
        LineItems.Clear();
        OnPropertyChanged(nameof(LineItemsCountLabel));
    }

    private void OnLineItemChanged()
    {
        if (_suppressLineRecalculate)
            return;

        RecalculateLineTotals();
    }

    private void RecalculateLineTotals()
    {
        if (_suppressLineRecalculate)
            return;

        _suppressLineRecalculate = true;
        try
        {
            var totalEx = 0m;
            var totalVat = 0m;
            var totalInc = 0m;

            foreach (var item in LineItems.OrderBy(x => x.LineNumber))
            {
                var quantity = item.Quantity <= 0m ? 1m : item.Quantity;
                var unitPrice = item.UnitPrice < 0m ? 0m : item.UnitPrice;

                var (lineEx, lineVat, lineInc) = CalculateLineTotals(quantity, unitPrice);
                item.AmountExVat = lineEx;
                item.VatAmount = lineVat;
                item.TotalAmountIncVat = lineInc;

                totalEx += lineEx;
                totalVat += lineVat;
                totalInc += lineInc;
            }

            AmountExVat = Math.Round(totalEx, 2);
            VatAmount = Math.Round(totalVat, 2);
            TotalAmountIncVat = Math.Round(totalInc, 2);
        }
        finally
        {
            _suppressLineRecalculate = false;
        }
    }

    private List<PreparedLineItem> BuildPreparedLineItems()
    {
        var prepared = new List<PreparedLineItem>();
        foreach (var item in LineItems.OrderBy(x => x.LineNumber))
        {
            var description = NormalizeSingleLine(item.Description);
            if (string.IsNullOrWhiteSpace(description))
                continue;

            var quantity = item.Quantity <= 0m ? 1m : item.Quantity;
            var unitPrice = item.UnitPrice < 0m ? 0m : item.UnitPrice;
            var (lineEx, lineVat, lineInc) = CalculateLineTotals(quantity, unitPrice);

            prepared.Add(new PreparedLineItem
            {
                LineNumber = prepared.Count + 1,
                Description = description,
                Quantity = quantity,
                UnitPrice = unitPrice,
                AmountExVat = lineEx,
                VatAmount = lineVat,
                TotalAmountIncVat = lineInc
            });
        }

        return prepared;
    }

    private (decimal amountExVat, decimal vatAmount, decimal totalAmountIncVat) CalculateLineTotals(decimal quantity, decimal unitPrice)
    {
        var effectiveRate = Math.Max(0m, VatRatePercent) / 100m;
        var baseAmount = Math.Round(quantity * unitPrice, 2);

        if (QuoteIncludesVat)
        {
            if (effectiveRate <= 0m)
                return (baseAmount, 0m, baseAmount);

            var exVat = Math.Round(baseAmount / (1m + effectiveRate), 2);
            var vat = Math.Round(baseAmount - exVat, 2);
            return (exVat, vat, baseAmount);
        }

        var vatAmount = Math.Round(baseAmount * effectiveRate, 2);
        var totalInc = Math.Round(baseAmount + vatAmount, 2);
        return (baseAmount, vatAmount, totalInc);
    }

    private void SelectRow(int id)
    {
        var row = Rows.FirstOrDefault(x => x.Id == id);
        if (row is not null)
            SelectedRow = row;
    }

    private static PurchaseOrderSupplier UpsertSupplier(OrdersDbContext db, string supplierName, bool includesVatDefault)
    {
        var nameNorm = NormalizeComparable(supplierName);
        var supplier = db.PurchaseOrderSuppliers
            .FirstOrDefault(x => x.NameNorm == nameNorm);

        if (supplier is null)
        {
            supplier = new PurchaseOrderSupplier();
            db.PurchaseOrderSuppliers.Add(supplier);
        }

        supplier.Name = supplierName;
        supplier.NameNorm = nameNorm;
        supplier.QuoteIncludesVatDefault = includesVatDefault;
        return supplier;
    }

    private static int GetNextOrderNumber(OrdersDbContext db)
    {
        var currentMax = db.PurchaseOrders
            .Select(x => (int?)x.OrderNumber)
            .Max()
            ?? 0;
        return currentMax + 1;
    }

    private static PurchaseOrderRow MapRow(PurchaseOrder entity)
    {
        return new PurchaseOrderRow
        {
            Id = entity.Id,
            OrderNumber = entity.OrderNumber,
            OrderReference = FormatOrderReference(entity.OrderNumber),
            OrderDate = ToLocal(entity.OrderDate),
            Supplier = entity.Supplier,
            Description = entity.Description,
            AmountExVat = entity.AmountExVat,
            VatAmount = entity.VatAmount,
            TotalAmountIncVat = entity.TotalAmountIncVat,
            Status = entity.Status,
            RequestedBy = entity.RequestedBy,
            Notes = entity.Notes
        };
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private static string BuildOrderSummary(IReadOnlyCollection<PreparedLineItem> items)
    {
        if (items.Count == 0)
            return string.Empty;

        var firstThree = items
            .OrderBy(x => x.LineNumber)
            .Take(3)
            .Select(x => x.Description)
            .ToList();

        var summary = string.Join(" | ", firstThree);
        if (items.Count > 3)
            summary += $" (+{items.Count - 3} more)";

        return summary;
    }

    private static string FormatOrderReference(int orderNumber)
    {
        return $"PO-{orderNumber:000000}";
    }

    private static bool ContainsIgnoreCase(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static DateTime ToUtc(DateTimeOffset? value)
    {
        return value?.UtcDateTime ?? DateTime.UtcNow;
    }

    private static DateTime ToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private sealed class PreparedLineItem
    {
        public int LineNumber { get; init; }
        public string Description { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal AmountExVat { get; init; }
        public decimal VatAmount { get; init; }
        public decimal TotalAmountIncVat { get; init; }
    }
}
