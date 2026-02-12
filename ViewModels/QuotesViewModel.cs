using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? ProductType { get; set; }
    public decimal AmountExVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmountIncVat { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class QuotesViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly Action _goJobCards;

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
        OnPropertyChanged(nameof(HasSelectedRow));
    }
    
    partial void OnPageNumberChanged(int value)
    {
        Load();
    }

    public QuotesViewModel(Window window, AppState appState, Action goJobCards, DateTime? startDate = null, DateTime? endDate = null)
    {
        _window = window;
        _appState = appState;
        _goJobCards = goJobCards;
        StatusOptions.Add("All");
        StatusOptions.AddRange(Enum.GetNames(typeof(QuoteStatus)));
        TypeOptions.Add("All");
        TypeOptions.AddRange(Enum.GetNames(typeof(QuoteType)));
        SetDefaultDateRange(startDate, endDate);
        Load();
    }

    public bool CanApproveQuotes => _appState.CanApproveQuotes;
    public bool CanExport => _appState.CanExport;

    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeChanged(string value) => ApplyFilters();
    partial void OnCompanyFilterChanged(string? value) => ApplyFilters();
    partial void OnRegistrationFilterChanged(string? value) => ApplyFilters();
    partial void OnStartDateChanged(DateTimeOffset? value) => ApplyFilters();
    partial void OnEndDateChanged(DateTimeOffset? value) => ApplyFilters();

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        int skip = (PageNumber - 1) * PageSize;

        var query = db.Quotes.AsNoTracking();

        if (!string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteStatus>(SelectedStatus, out var status))
        {
            query = query.Where(q => q.Status == status);
        }

        if (!string.Equals(SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<QuoteType>(SelectedType, out var type))
        {
            query = query.Where(q => q.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(CompanyFilter))
        {
            var s = CompanyFilter.Trim();
            query = query.Where(q => q.Company.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(RegistrationFilter))
        {
            var s = RegistrationFilter.Trim();
            query = query.Where(q => q.Registration != null && q.Registration.Contains(s));
        }

        if (StartDate != null)
        {
            var start = StartDate.Value.Date;
            query = query.Where(q => q.CreatedAt >= start);
        }

        if (EndDate != null)
        {
            var endExclusive = EndDate.Value.Date.AddDays(1);
            query = query.Where(q => q.CreatedAt < endExclusive);
        }

        TotalCount = query.Count();

        var items = query
            .OrderByDescending(q => q.CreatedAt)
            .Skip(skip)
            .Take(PageSize)
            .ToList();
        
        var pricingService = new QuotePricingService(_appState.Settings);

        Rows.Clear();
        foreach (var q in items)
        {
            var priceResult = pricingService.CalculatePrice(q);

            Rows.Add(new QuoteRow
            {
                Id = q.Id,
                QuoteNumber = q.QuoteNumber,
                Type = q.Type.ToString(),
                Status = q.Status.ToString(),
                Company = q.Company,
                Registration = q.Registration,
                ProductType = q.ProductType,
                AmountExVat = q.AmountExVat,
                VatAmount = priceResult.VatAmount,
                AmountIncVat = priceResult.AmountIncVat,
                CreatedAt = q.CreatedAt
            });
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

        Load();
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
        Load();
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
            var maxQuoteNumber = db.Quotes.Any() ? db.Quotes.Max(x => x.QuoteNumber) : 0;
            quote.QuoteNumber = maxQuoteNumber + 1;

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

            _appState.SetStatus($"Sample quote #{quote.QuoteNumber} created successfully with all required information.");
            Load();
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
        Load();
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
    private void ApproveSelected()
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

        // Approve the quote
        var wf = new WorkflowService();
        var (jobId, errorMessage) = wf.ApproveQuote(SelectedRow.Id, _appState.OperatorName, scheduleDate: null);
        if (jobId == 0)
        {
            _appState.SetStatus($"Approval blocked: {errorMessage}");
            return;
        }
        _appState.SetStatus("Quote approved. Job card created. You can set the schedule from the Job Cards view.");
        Load();
    }

    public bool CanApproveSelectedQuote => CanApproveQuotes && SelectedRow != null;
    public bool HasSelectedRow => SelectedRow != null;

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

        using var db = new AppDbContext();
        var quote = db.Quotes.Include(q => q.LineItems).FirstOrDefault(q => q.Id == SelectedRow.Id);
        if (quote is null) return;

        var pdfBytes = await Task.Run(() => new QuotePdfService(_appState.Settings).BuildQuotePdf(quote));
        var tempPath = Path.GetTempFileName() + ".pdf";
        File.WriteAllBytes(tempPath, pdfBytes);

        new AttachmentStorageService().AddAttachment(
            _appState.OperatorName,
            AttachmentOwnerType.Quote,
            quote.Id,
            AttachmentKind.QuotePdf,
            tempPath);

        try { File.Delete(tempPath); } catch { }

        _appState.SetStatus("Quote PDF generated and stored as attachment.");
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRow is null) return;

        using var db = new AppDbContext();
        var quote = db.Quotes.Include(q => q.LineItems).FirstOrDefault(q => q.Id == SelectedRow.Id);
        if (quote is null) return;

        // Check if there's a linked job card
        var job = db.JobCards.FirstOrDefault(j => j.QuoteId == quote.Id);
        if (job != null)
        {
            // Check if the job has been completed
            if (job.Status == JobStatus.Completed)
            {
                _appState.SetStatus("Cannot delete: Quote has a completed job.");
                return;
            }
            // Job exists but not completed - still need to delete it
            // Delete the job card first
            db.JobCards.Remove(job);
        }

        // Delete any linked cancellation entries
        var cancellations = db.CancellationEntries.Where(c => c.QuoteId == quote.Id).ToList();
        foreach (var c in cancellations)
        {
            db.CancellationEntries.Remove(c);
        }

        // Delete any attachments
        var attachments = db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.Quote && a.OwnerId == quote.Id)
            .ToList();
        foreach (var a in attachments)
        {
            db.Attachments.Remove(a);
        }

        // Delete the quote
        db.Quotes.Remove(quote);
        db.SaveChanges();

        _appState.SetStatus("Quote deleted.");
        Load();
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        if (SelectedRow is null) return;

        using var db = new AppDbContext();
        var quote = db.Quotes.Include(q => q.LineItems).FirstOrDefault(q => q.Id == SelectedRow.Id);
        if (quote is null) return;

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Quote PDF",
            SuggestedFileName = $"Quote_{quote.Id}_{quote.Registration}.pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
            ]
        });

        if (file is null) return;

        var pdfBytes = await Task.Run(() => new QuotePdfService(_appState.Settings).BuildQuotePdf(quote));
        await File.WriteAllBytesAsync(file.Path.LocalPath, pdfBytes);

        _appState.SetStatus($"Quote PDF saved: {Path.GetFileName(file.Path.LocalPath)}");
    }
}
