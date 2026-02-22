using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class QuoteRow : ObservableObject
{
    public int Id { get; set; }
    public int QuoteNumber { get; set; }
    public string QuoteReference => QuoteReferenceFormatter.Format(QuoteNumber);
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? ProductType { get; set; }
    public decimal AmountExVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmountIncVat { get; set; }
    public string WorkflowBadge { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public partial class QuotesViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly Action _goJobCards;
    private readonly IDataStore _dataStore;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<QuoteRow> Rows { get; } = new();

    [ObservableProperty] 
    private QuoteRow? selectedRow;
    
    [ObservableProperty]
    private int pageNumber = 1;
    
    [ObservableProperty]
    private int pageSize = 100;
    
    [ObservableProperty]
    private int totalCount = 0;

    public List<string> StatusOptions { get; } = new();
    public List<string> TypeOptions { get; } = new();

    [ObservableProperty] private string selectedStatus = "All";
    [ObservableProperty] private string selectedType = "All";
    [ObservableProperty] private string? companyFilter;
    [ObservableProperty] private string? registrationFilter;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;
    
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    public bool CanNextPage => PageNumber < TotalPages;
    public bool CanPrevPage => PageNumber > 1;

    partial void OnSelectedRowChanged(QuoteRow? value)
    {
        OnPropertyChanged(nameof(CanApproveSelectedQuote));
        OnPropertyChanged(nameof(CanCancelSelectedQuote));
        OnPropertyChanged(nameof(HasSelectedRow));
    }
    
    partial void OnPageNumberChanged(int value)
    {
        _ = Load();
    }

    public QuotesViewModel(Window window, AppState appState, Action goJobCards, DateTime? startDate = null, DateTime? endDate = null)
    {
        _window = window;
        _appState = appState;
        _goJobCards = goJobCards;
        _dataStore = DataStoreFactory.Create(_appState.Settings);
        StatusOptions.Add("All");
        StatusOptions.AddRange(Enum.GetNames(typeof(QuoteStatus)));
        TypeOptions.Add("All");
        TypeOptions.AddRange(Enum.GetNames(typeof(QuoteType)));
        SetDefaultDateRange(startDate, endDate);
        _ = Load();
    }

    public bool CanApproveQuotes => _appState.CanApproveQuotes;

    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeChanged(string value) => ApplyFilters();
    partial void OnCompanyFilterChanged(string? value) => ApplyFilters();
    partial void OnRegistrationFilterChanged(string? value) => ApplyFilters();
    partial void OnStartDateChanged(DateTimeOffset? value) => ApplyFilters();
    partial void OnEndDateChanged(DateTimeOffset? value) => ApplyFilters();

    [RelayCommand]
    private async Task Load()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var query = new QuoteQuery
            {
                SelectedStatus = SelectedStatus,
                SelectedType = SelectedType,
                CompanyFilter = CompanyFilter,
                RegistrationFilter = RegistrationFilter,
                StartDate = StartDate,
                EndDate = EndDate,
                PageNumber = PageNumber,
                PageSize = PageSize
            };

            var page = await _dataStore.GetQuotesAsync(query, token);
            var pricingService = new QuotePricingService(_appState.Settings);
            var workflowBadges = BuildWorkflowBadges(page.Items.Select(x => x.Id).ToList());

            TotalCount = page.TotalCount;

            Rows.Clear();
            foreach (var q in page.Items)
            {
                var quote = new Quote
                {
                    AmountExVat = q.AmountExVat
                };
                var priceResult = pricingService.CalculatePrice(quote);

                Rows.Add(new QuoteRow
                {
                    Id = q.Id,
                    QuoteNumber = q.QuoteNumber,
                    Type = q.Type,
                    Status = q.Status,
                    Company = q.Company,
                    Registration = q.Registration ?? string.Empty,
                    ProductType = q.ProductType,
                    AmountExVat = q.AmountExVat,
                    VatAmount = priceResult.VatAmount,
                    AmountIncVat = priceResult.AmountIncVat,
                    WorkflowBadge = workflowBadges.TryGetValue(q.Id, out var badge) ? badge : string.Empty,
                    CreatedAt = q.CreatedAt
                });
            }

            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(CanPrevPage));
        }
        catch (OperationCanceledException)
        {
            // Newer load superseded this request.
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading quotes: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedStatus = "All";
        SelectedType = "All";
        CompanyFilter = null;
        RegistrationFilter = null;
        SetDefaultDateRange(null, null);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (PageNumber != 1)
        {
            PageNumber = 1;
            return;
        }

        _ = Load();
    }

    private static Dictionary<int, string> BuildWorkflowBadges(List<int> quoteIds)
    {
        var result = new Dictionary<int, string>();
        if (quoteIds.Count == 0)
            return result;

        using var db = new AppDbContext();

        var quotes = db.Quotes.AsNoTracking()
            .Where(q => quoteIds.Contains(q.Id))
            .Select(q => new QuoteBadgeMeta
            {
                Id = q.Id,
                Type = q.Type,
                Status = q.Status,
                IncludesAppLiveTracking = q.IncludesAppLiveTracking,
                ProductType = q.ProductType,
                Notes = q.Notes
            })
            .ToList();

        var lineItemsByQuote = db.QuoteLineItems.AsNoTracking()
            .Where(li => quoteIds.Contains(li.QuoteId))
            .Select(li => new QuoteLineBadgeMeta
            {
                QuoteId = li.QuoteId,
                ProductType = li.ProductType,
                ProductName = li.ProductName,
                ProductCode = li.ProductCode,
                IncludesAppLiveTracking = li.IncludesAppLiveTracking
            })
            .ToList()
            .GroupBy(li => li.QuoteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QuoteLineBadgeMeta>)g.ToList());

        var quoteIdsWithJobCards = db.JobCards.AsNoTracking()
            .Where(j => j.QuoteId.HasValue && quoteIds.Contains(j.QuoteId.Value))
            .Select(j => j.QuoteId!.Value)
            .Distinct()
            .ToHashSet();

        foreach (var quote in quotes)
        {
            if (quote.Status != QuoteStatus.Approved)
                continue;

            if (quoteIdsWithJobCards.Contains(quote.Id))
                continue;

            var lineItems = lineItemsByQuote.TryGetValue(quote.Id, out var rows)
                ? rows
                : Array.Empty<QuoteLineBadgeMeta>();

            if (IsLiveTrackingOnlyQuote(quote, lineItems))
            {
                result[quote.Id] = "No Job Card (Live Tracking Only)";
                continue;
            }

            if (quote.Type == QuoteType.Removal
                && HasWorkflowMarker(quote.Notes, WorkflowService.NoRemovalJobCardMarker))
            {
                result[quote.Id] = "No Job Card (Out of Warranty Removal)";
                continue;
            }

            if (quote.Type == QuoteType.Install && IsTransferFeeOnlyQuote(lineItems))
            {
                result[quote.Id] = "No Job Card (Transfer Fee Only)";
            }
        }

        return result;
    }

    private static bool IsLiveTrackingOnlyQuote(QuoteBadgeMeta quote, IReadOnlyList<QuoteLineBadgeMeta> lineItems)
    {
        if (!HasLiveTracking(quote, lineItems))
            return false;

        if (lineItems.Count > 0)
            return !lineItems.Any(IsUnitLineItem);

        return !IsUnitProductType(quote.ProductType);
    }

    private static bool HasLiveTracking(QuoteBadgeMeta quote, IReadOnlyList<QuoteLineBadgeMeta> lineItems)
    {
        if (quote.IncludesAppLiveTracking)
            return true;

        if (lineItems.Count > 0)
            return lineItems.Any(IsLiveTrackingLineItem);

        return ContainsLiveTracking(quote.ProductType);
    }

    private static bool IsUnitLineItem(QuoteLineBadgeMeta item)
    {
        if (IsLiveTrackingLineItem(item))
            return false;

        return IsUnitProductType(item.ProductType)
               || IsUnitProductType(item.ProductName)
               || IsUnitProductType(item.ProductCode);
    }

    private static bool IsLiveTrackingLineItem(QuoteLineBadgeMeta item)
    {
        if (item.IncludesAppLiveTracking)
            return true;

        if (string.Equals(item.ProductCode, "APP-LIVE-TRACKING", StringComparison.OrdinalIgnoreCase))
            return true;

        return ContainsLiveTracking(item.ProductType)
               || ContainsLiveTracking(item.ProductName)
               || ContainsLiveTracking(item.ProductCode);
    }

    private static bool IsTransferFeeOnlyQuote(IReadOnlyList<QuoteLineBadgeMeta> lineItems)
    {
        if (lineItems.Count == 0)
            return false;

        var meaningfulLines = lineItems
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode)
                || !string.IsNullOrWhiteSpace(item.ProductName)
                || !string.IsNullOrWhiteSpace(item.ProductType))
            .ToList();

        if (meaningfulLines.Count == 0)
            return false;

        return meaningfulLines.All(IsTransferInstallFeeLineItem);
    }

    private static bool IsTransferInstallFeeLineItem(QuoteLineBadgeMeta item)
    {
        if (string.Equals(item.ProductCode, WorkflowService.TransferInstallFeeCode, StringComparison.OrdinalIgnoreCase))
            return true;

        return ContainsTransferInstallFee(item.ProductType)
               || ContainsTransferInstallFee(item.ProductName);
    }

    private static bool IsUnitProductType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToUpperInvariant();
        if (!normalized.Contains("STING", StringComparison.Ordinal))
            return false;

        if (normalized.Contains("LIVE TRACKING", StringComparison.Ordinal))
            return false;

        if (normalized.Contains("MONTHLY", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool ContainsLiveTracking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.IndexOf("LIVE TRACKING", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsTransferInstallFee(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Contains("TRANSFER", StringComparison.Ordinal)
               && normalized.Contains("INSTALL", StringComparison.Ordinal);
    }

    private static bool HasWorkflowMarker(string? notes, string marker)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return false;

        return notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class QuoteBadgeMeta
    {
        public int Id { get; init; }
        public QuoteType Type { get; init; }
        public QuoteStatus Status { get; init; }
        public bool IncludesAppLiveTracking { get; init; }
        public string? ProductType { get; init; }
        public string? Notes { get; init; }
    }

    private sealed class QuoteLineBadgeMeta
    {
        public int QuoteId { get; init; }
        public string ProductType { get; init; } = string.Empty;
        public string? ProductName { get; init; }
        public string? ProductCode { get; init; }
        public bool IncludesAppLiveTracking { get; init; }
    }

    private void SetDefaultDateRange(DateTime? start, DateTime? end)
    {
        if (start != null || end != null)
        {
            StartDate = start != null ? new DateTimeOffset(start.Value.Date) : null;
            EndDate = end != null ? new DateTimeOffset(end.Value.Date) : null;
            return;
        }

        var today = DateTime.Today;
        StartDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        EndDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));
    }

    [RelayCommand]
    private async Task AddQuote()
    {
        var dlg = new StingListManager.Views.QuoteEditWindow();
        var pricingService = new QuotePricingService(_appState.Settings);
        dlg.DataContext = new QuoteEditViewModel(() => dlg.Close(), null, _appState, pricingService);
        await dlg.ShowDialog(_window);
        await Load();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (PageNumber < TotalPages)
            PageNumber++;
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (PageNumber > 1)
            PageNumber--;
    }

    [RelayCommand]
    private void CreateSampleQuote()
    {
        try
        {
            using var db = new AppDbContext();

            // Create a sample INSTALL quote with complete information
            var quote = new Quote
            {
                Type = QuoteType.Install,
                Status = QuoteStatus.Draft,
                Company = "Sample Company Ltd",
                Registration = "REG123",
                FleetNumber = "FLEET-001",
                Make = "Toyota",
                Model = "Hiace",
                Colour = "White",
                VinNumber = "JTEKV5H61F2345678",
                TrackingUnitMake = "Teltonika",
                Imei = "352656106478915",
                SerialNumber = "TK102-123456",
                Iccid = "8944700102198305179",
                SimNumber = "27123456789",
                Notes = "Sample installation quote with all required fields populated. This demonstrates a complete data entry.",
                CreatedAt = DateTime.UtcNow
            };

            // Generate quote number
            quote.QuoteNumber = QuoteNumberAllocator.GetNext(db);

            // Add line items with sample products
            quote.LineItems = new System.Collections.Generic.List<QuoteLineItem>
            {
                new QuoteLineItem
                {
                    LineNumber = 1,
                    ProductType = "STING",
                    ProductCode = "STING-001",
                    ProductName = "STING Basic Unit",
                    Quantity = 1,
                    UnitPriceExVat = 1250m,
                    LineTotalExVat = 1250m,
                    IsVatExempt = false,
                    IncludesPanicButton = false,
                    IncludesAppLiveTracking = false,
                    Description = "GPS tracking unit with basic features"
                },
                new QuoteLineItem
                {
                    LineNumber = 2,
                    ProductType = "PLUS",
                    ProductCode = "STING-PLUS-001",
                    ProductName = "STING Plus Features",
                    Quantity = 1,
                    UnitPriceExVat = 300m,
                    LineTotalExVat = 300m,
                    IsVatExempt = false,
                    IncludesPanicButton = true,
                    IncludesAppLiveTracking = true,
                    Description = "Panic button and live tracking features"
                },
                new QuoteLineItem
                {
                    LineNumber = 3,
                    ProductType = "INSTALLATION",
                    ProductCode = "INSTALL-001",
                    ProductName = "Installation Service",
                    Quantity = 1,
                    UnitPriceExVat = 250m,
                    LineTotalExVat = 250m,
                    IsVatExempt = false,
                    IncludesPanicButton = false,
                    IncludesAppLiveTracking = false,
                    Description = "Professional installation of tracking unit"
                }
            };

            // Calculate totals
            var subtotal = quote.LineItems.Sum(x => x.LineTotalExVat);
            var vatRate = _appState.Settings.VatRate;
            var vatableAmount = quote.LineItems.Where(x => !x.IsVatExempt).Sum(x => x.LineTotalExVat);
            var vat = vatableAmount * vatRate;

            quote.AmountExVat = subtotal;

            db.Quotes.Add(quote);
            db.SaveChanges();

            _appState.SetStatus($"Sample quote {QuoteReferenceFormatter.Format(quote.QuoteNumber)} created successfully with all required information.");
            _ = Load();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error creating sample quote: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.QuoteEditWindow();
        var pricingService = new QuotePricingService(_appState.Settings);
        dlg.DataContext = new QuoteEditViewModel(() => dlg.Close(), SelectedRow.Id, _appState, pricingService);
        await dlg.ShowDialog(_window);
        await Load();
    }

    [RelayCommand]
    private async Task ViewDetails()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.QuoteDetailsWindow();
        dlg.DataContext = new QuoteDetailsViewModel(() => dlg.Close(), SelectedRow.Id, _appState);
        await dlg.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task OpenSelectedFromDoubleClick()
    {
        if (SelectedRow is null) return;

        if (string.Equals(SelectedRow.Status, QuoteStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            await ViewRelatedJobCards();
            return;
        }

        await ViewDetails();
    }

    [RelayCommand]
    private async Task ViewRelatedJobCards()
    {
        if (SelectedRow is null) return;

        var hasRelatedJobCards = await _dataStore.HasRelatedJobCardsForQuoteAsync(SelectedRow.Id);
        if (!hasRelatedJobCards)
        {
            _appState.SetStatus("No related job cards found for the selected quote.");
            return;
        }

        var wnd = new StingListManager.Views.DocumentsWindow
        {
            Title = $"Related Job Cards - {SelectedRow.QuoteReference}",
            Width = 1300,
            Height = 700
        };

        var vm = new JobCardsViewModel(_window, _appState, quoteId: SelectedRow.Id);
        var view = new StingListManager.Views.JobCardsView { DataContext = vm };
        wnd.Content = view;

        await wnd.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task ApproveSelected()
    {
        if (SelectedRow is null)
        {
            _appState.SetStatus("Please select a quote to approve.");
            return;
        }

        if (!CanApproveQuotes)
        {
            _appState.SetStatus("You don't have permission to approve quotes.");
            return;
        }

        var result = await _dataStore.ApproveQuoteAsync(SelectedRow.Id, _appState.OperatorName, scheduleDate: null);
        if (!result.Success)
        {
            _appState.SetStatus($"Approval blocked: {result.Message}");
            return;
        }

        _appState.SetStatus(result.Message);
        await Load();
    }

    public bool CanApproveSelectedQuote => CanApproveQuotes && SelectedRow != null;
    public bool CanCancelSelectedQuote =>
        SelectedRow != null &&
        string.Equals(SelectedRow.Status, QuoteStatus.Draft.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool HasSelectedRow => SelectedRow != null;

    [RelayCommand]
    private async Task CancelSelected()
    {
        if (SelectedRow is null)
        {
            _appState.SetStatus("Please select a quote to cancel.");
            return;
        }

        var result = await _dataStore.CancelDraftQuoteAsync(SelectedRow.Id);
        if (!result.Success)
        {
            _appState.SetStatus(result.Message);
            return;
        }

        var quoteRef = QuoteReferenceFormatter.Format(result.QuoteNumber);
        new AuditService().Log(
            _appState.OperatorName,
            "QUOTE_CANCEL",
            "Quote",
            result.QuoteId,
            result.Registration,
            $"Draft quote {quoteRef} cancelled");

        _appState.SetStatus($"Quote {quoteRef} cancelled.");
        await Load();
    }

    [RelayCommand]
    private async Task OpenDocuments()
    {
        if (SelectedRow is null) return;

        var wnd = new StingListManager.Views.DocumentsWindow();
        var vm = new QuoteDocumentsViewModel(_window, _appState, SelectedRow.Id);
        var view = new StingListManager.Views.QuoteDocumentsView { DataContext = vm };
        wnd.Content = view;
        await wnd.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task GenerateQuotePdf()
    {
        if (SelectedRow is null) return;

        var quote = await _dataStore.GetQuoteWithLineItemsAsync(SelectedRow.Id);
        if (quote is null) return;

        var pdfBytes = await Task.Run(() => new QuotePdfService(_appState.Settings).BuildQuotePdf(quote));
        var tempPath = Path.GetTempFileName() + ".pdf";
        File.WriteAllBytes(tempPath, pdfBytes);

        var quoteReference = QuoteReferenceFormatter.Format(quote.QuoteNumber);
        var pdfFileName = $"Quote_{quoteReference}.pdf";

        var attachmentService = new AttachmentStorageService();
        var attachment = attachmentService.AddAttachment(
            _appState.OperatorName,
            AttachmentOwnerType.Quote,
            quote.Id,
            AttachmentKind.QuotePdf,
            tempPath,
            preferredFileName: pdfFileName);

        try { File.Delete(tempPath); } catch { }

        try
        {
            attachmentService.OpenAttachment(attachment.StoredPath);
            _appState.SetStatus($"Quote PDF generated as {attachment.FileName}, stored in Generated\\Quotes, and opened.");
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Quote PDF generated and stored as attachment, but could not be opened: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (SelectedRow is null) return;

        var result = await _dataStore.DeleteQuoteAsync(SelectedRow.Id);
        _appState.SetStatus(result.Message, !result.Success);
        if (result.Success)
            await Load();
    }

}
