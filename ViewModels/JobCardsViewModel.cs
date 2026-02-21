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

public partial class JobCardRow : ObservableObject
{
    public int Id { get; set; }
    public int JobCardNumber { get; set; }
    public string JobCardReference { get; set; } = "";
    public int? QuoteId { get; set; }
    public string QuoteReference { get; set; } = "-";
    public JobType JobTypeValue { get; set; }
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class JobCardsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly int? _quoteIdFilter;

    public ObservableCollection<JobCardRow> Rows { get; } = new();

    [ObservableProperty] private JobCardRow? selectedRow;
    [ObservableProperty] private List<JobCardRow>? selectedRows;

    public bool HasSelectedRows => SelectedRows != null && SelectedRows.Count > 0;

    public List<string> StatusOptions { get; } = new();
    public List<string> TypeOptions { get; } = new();

    [ObservableProperty] private string selectedStatus = "All";
    [ObservableProperty] private string selectedType = "All";
    [ObservableProperty] private string? companyFilter;
    [ObservableProperty] private string? registrationFilter;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;

    public JobCardsViewModel(Window window, AppState appState, DateTime? startDate = null, DateTime? endDate = null, int? quoteId = null)
    {
        _window = window;
        _appState = appState;
        _quoteIdFilter = quoteId;
        StatusOptions.Add("All");
        StatusOptions.AddRange(Enum.GetNames(typeof(JobStatus)));
        TypeOptions.Add("All");
        TypeOptions.AddRange(Enum.GetNames(typeof(JobType)));
        SetDefaultDateRange(startDate, endDate);
        Load();
    }

    partial void OnSelectedRowsChanged(List<JobCardRow>? value)
    {
        OnPropertyChanged(nameof(HasSelectedRows));
    }

    public bool CanCompleteJobs => _appState.CanCompleteJobs;

    partial void OnSelectedStatusChanged(string value) => Load();
    partial void OnSelectedTypeChanged(string value) => Load();
    partial void OnCompanyFilterChanged(string? value) => Load();
    partial void OnRegistrationFilterChanged(string? value) => Load();
    partial void OnStartDateChanged(DateTimeOffset? value) => Load();
    partial void OnEndDateChanged(DateTimeOffset? value) => Load();

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var query = db.JobCards.AsNoTracking();

        if (!string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobStatus>(SelectedStatus, out var status))
        {
            query = query.Where(j => j.Status == status);
        }

        if (!string.Equals(SelectedType, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<JobType>(SelectedType, out var type))
        {
            query = query.Where(j => j.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(CompanyFilter))
        {
            var s = CompanyFilter.Trim();
            query = query.Where(j => j.Company.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(RegistrationFilter))
        {
            var s = RegistrationFilter.Trim();
            query = query.Where(j => j.Registration.Contains(s));
        }

        if (StartDate != null)
        {
            var start = StartDate.Value.Date;
            query = query.Where(j => j.CreatedAt >= start);
        }

        if (EndDate != null)
        {
            var endExclusive = EndDate.Value.Date.AddDays(1);
            query = query.Where(j => j.CreatedAt < endExclusive);
        }

        if (_quoteIdFilter.HasValue)
        {
            var quoteId = _quoteIdFilter.Value;
            query = query.Where(j => j.QuoteId == quoteId);
        }

        var items = _quoteIdFilter.HasValue
            ? query.OrderBy(j => j.JobCardNumber).ThenBy(j => j.CreatedAt).ToList()
            : query.OrderByDescending(j => j.CreatedAt).ToList();

        var quoteIds = items
            .Where(j => j.QuoteId.HasValue)
            .Select(j => j.QuoteId!.Value)
            .Distinct()
            .ToList();

        var quoteRefById = db.Quotes.AsNoTracking()
            .Where(q => quoteIds.Contains(q.Id))
            .Select(q => new { q.Id, q.QuoteNumber })
            .ToList()
            .ToDictionary(q => q.Id, q => QuoteReferenceFormatter.Format(q.QuoteNumber));

        Rows.Clear();
        foreach (var j in items)
        {
            var quoteRef = "-";
            if (j.QuoteId.HasValue && quoteRefById.TryGetValue(j.QuoteId.Value, out var formattedRef))
                quoteRef = formattedRef;

            Rows.Add(new JobCardRow
            {
                Id = j.Id,
                JobCardNumber = j.JobCardNumber,
                JobCardReference = JobCardReferenceFormatter.Format(j.Type, j.JobCardNumber),
                QuoteId = j.QuoteId,
                QuoteReference = quoteRef,
                JobTypeValue = j.Type,
                Type = j.Type.ToString(),
                Status = j.Status.ToString(),
                Company = j.Company,
                Registration = j.Registration,
                Make = j.Make,
                Model = j.Model,
                Imei = j.Imei,
                SerialNumber = j.SerialNumber,
                Iccid = j.Iccid,
                CreatedAt = j.CreatedAt
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
    private async Task EditSelected()
    {
        var row = ResolveSelectedRow();
        if (row is null) return;

        var dlg = new StingListManager.Views.JobCardEditWindow();
        dlg.DataContext = new JobCardEditViewModel(row.Id, () => dlg.Close(), _appState);

        await dlg.ShowDialog(_window);

        Load();
    }

    [RelayCommand]
    private async Task CompleteSelected()
    {
        if (!CanCompleteJobs)
        {
            _appState.SetStatus("Not permitted.");
            await DialogService.Alert(_window, "Not Permitted", "You do not have permission to complete job cards.");
            return;
        }

        var row = ResolveSelectedRow();
        if (row is null)
        {
            _appState.SetStatus("Please select a job card to complete.");
            await DialogService.Alert(_window, "No Selection", "Please select a job card first.");
            return;
        }

        try
        {
            _appState.SetStatus($"Completing {row.JobCardReference}...");

            var wf = new WorkflowService();
            var result = await wf.CompleteJobCardAsync(row.Id, _appState.OperatorName, _appState.Settings.WialonApiToken);
            _appState.SetStatus(result.message, !result.ok);

            if (!result.ok)
            {
                await DialogService.Alert(_window, "Complete Job Card Failed", result.message);
                return;
            }

            Load();
        }
        catch (Exception ex)
        {
            var message = $"Error completing job card: {ex.Message}";
            _appState.SetStatus(message, true);
            await DialogService.Alert(_window, "Complete Job Card Failed", message);
        }
    }

    [RelayCommand]
    private async Task OpenDocuments()
    {
        var row = ResolveSelectedRow();
        if (row is null) return;

        var wnd = new StingListManager.Views.DocumentsWindow();
        var vm = new JobCardDocumentsViewModel(_window, _appState, row.Id);

        var view = new StingListManager.Views.JobCardDocumentsView
        {
            DataContext = vm
        };

        wnd.Content = view;
        await wnd.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task ExportSelectedToPdf()
    {
        if (SelectedRows == null || SelectedRows.Count == 0)
        {
            _appState.SetStatus("Please select one or more job cards to export.");
            return;
        }

        try
        {
            using var db = new AppDbContext();
            
            var selectedIds = SelectedRows.Select(r => r.Id).ToList();
            var jobCards = db.JobCards
                .Where(j => selectedIds.Contains(j.Id))
                .OrderBy(j => j.JobCardNumber)
                .ToList();

            if (jobCards.Count == 0)
            {
                _appState.SetStatus("No job cards found.");
                return;
            }

            var pdfService = new JobCardPdfService();
            byte[] pdfBytes;

            if (jobCards.Count == 1)
            {
                pdfBytes = pdfService.BuildJobCardPdf(jobCards[0]);
            }
            else
            {
                pdfBytes = pdfService.BuildMultipleJobCardsPdf(jobCards);
            }

            var firstJob = jobCards.First();
            var lastJob = jobCards.Last();
            var firstReference = JobCardReferenceFormatter.Format(firstJob.Type, firstJob.JobCardNumber);
            var lastReference = JobCardReferenceFormatter.Format(lastJob.Type, lastJob.JobCardNumber);
            var suggestedFileName = jobCards.Count == 1
                ? $"JobCard_{firstReference}.pdf"
                : $"JobCards_{firstReference}-{lastReference}.pdf";

            var managedPath = AttachmentStorageService.BuildUniqueFilePath(Paths.GeneratedJobCardsDir, suggestedFileName);
            await File.WriteAllBytesAsync(managedPath, pdfBytes);

            var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Job Card PDF",
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } }
                }
            });

            if (file is null)
            {
                _appState.SetStatus($"Generated {jobCards.Count} job card PDF(s) in Generated\\JobCards: {Path.GetFileName(managedPath)}");
                return;
            }

            await File.WriteAllBytesAsync(file.Path.LocalPath, pdfBytes);

            _appState.SetStatus(
                $"Exported {jobCards.Count} job card(s) to PDF: {Path.GetFileName(file.Path.LocalPath)}. " +
                $"Managed copy: {Path.GetFileName(managedPath)}");
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error exporting PDF: {ex.Message}");
        }
    }

    private JobCardRow? ResolveSelectedRow()
    {
        if (SelectedRow != null)
            return SelectedRow;

        return SelectedRows?.FirstOrDefault();
    }
}
