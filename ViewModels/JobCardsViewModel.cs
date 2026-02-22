using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    public int PhotoCount { get; set; }
    public DateTime? LastPhotoAt { get; set; }
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
    private readonly IDataStore _dataStore;
    private readonly int? _quoteIdFilter;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterDebounceCts;
    private bool _suppressAutoLoad;

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
        _dataStore = DataStoreFactory.Create(_appState.Settings);
        _quoteIdFilter = quoteId;
        _suppressAutoLoad = true;
        StatusOptions.Add("All");
        StatusOptions.AddRange(Enum.GetNames(typeof(JobStatus)));
        TypeOptions.Add("All");
        TypeOptions.AddRange(Enum.GetNames(typeof(JobType)));
        SetDefaultDateRange(startDate, endDate);
        _suppressAutoLoad = false;
        _ = Load();
    }

    partial void OnSelectedRowsChanged(List<JobCardRow>? value)
    {
        OnPropertyChanged(nameof(HasSelectedRows));
    }

    public bool CanCompleteJobs => _appState.CanCompleteJobs;

    partial void OnSelectedStatusChanged(string value)
    {
        if (_suppressAutoLoad) return;
        _ = Load();
    }

    partial void OnSelectedTypeChanged(string value)
    {
        if (_suppressAutoLoad) return;
        _ = Load();
    }

    partial void OnCompanyFilterChanged(string? value) => DebounceLoad();
    partial void OnRegistrationFilterChanged(string? value) => DebounceLoad();

    partial void OnStartDateChanged(DateTimeOffset? value)
    {
        if (_suppressAutoLoad) return;
        _ = Load();
    }

    partial void OnEndDateChanged(DateTimeOffset? value)
    {
        if (_suppressAutoLoad) return;
        _ = Load();
    }

    [RelayCommand]
    private async Task Load()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var query = new JobCardQuery
            {
                SelectedStatus = SelectedStatus,
                SelectedType = SelectedType,
                CompanyFilter = CompanyFilter,
                RegistrationFilter = RegistrationFilter,
                StartDate = StartDate,
                EndDate = EndDate,
                QuoteIdFilter = _quoteIdFilter
            };

            var items = await _dataStore.GetJobCardsAsync(query, token);

            Rows.Clear();
            foreach (var j in items)
            {
                Rows.Add(new JobCardRow
                {
                    Id = j.Id,
                    JobCardNumber = j.JobCardNumber,
                    JobCardReference = j.JobCardReference,
                    QuoteId = j.QuoteId,
                    QuoteReference = j.QuoteReference,
                    JobTypeValue = j.JobTypeValue,
                    Type = j.Type,
                    Status = j.Status,
                    PhotoCount = j.PhotoCount,
                    LastPhotoAt = j.LastPhotoAt,
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
        catch (OperationCanceledException)
        {
            // Newer load superseded this call.
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading job cards: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressAutoLoad = true;
        SelectedStatus = "All";
        SelectedType = "All";
        CompanyFilter = null;
        RegistrationFilter = null;
        SetDefaultDateRange(null, null);
        _suppressAutoLoad = false;
        _ = Load();
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

    private async void DebounceLoad()
    {
        if (_suppressAutoLoad)
            return;

        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _filterDebounceCts = cts;

        try
        {
            await Task.Delay(250, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(async () => await Load());
        }
        catch (OperationCanceledException)
        {
            // Newer filter value superseded this load.
        }
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        var row = ResolveSelectedRow();
        if (row is null) return;

        var dlg = new StingListManager.Views.JobCardEditWindow();
        dlg.DataContext = new JobCardEditViewModel(row.Id, () => dlg.Close(), _appState);

        await dlg.ShowDialog(_window);

        await Load();
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
            if (row.JobTypeValue is JobType.Install or JobType.Transfer)
            {
                var photoCount = await _dataStore.CountJobCardPhotosAsync(row.Id);

                if (photoCount <= 0)
                {
                    var missingPhotosMessage = "Upload at least one technician photo before completing this job card.";
                    _appState.SetStatus(missingPhotosMessage, true);
                    await DialogService.Alert(_window, "Technician Photos Required", missingPhotosMessage);
                    return;
                }
            }

            _appState.SetStatus($"Completing {row.JobCardReference}...");

            var wf = new WorkflowService();
            var result = await wf.CompleteJobCardAsync(row.Id, _appState.OperatorName, _appState.Settings.WialonApiToken);

            if (!result.ok)
            {
                _appState.SetStatus(result.message, true);
                await DialogService.Alert(_window, "Complete Job Card Failed", result.message);
                return;
            }

            var completionParts = JobCompletionNotificationParser.Parse(result.message);
            _appState.SetStatus(completionParts.PrimaryMessage, false);

            foreach (var info in completionParts.IntegrationInfo)
            {
                _appState.SetStatus($"Integration: {info}", false);
            }

            foreach (var warning in completionParts.IntegrationWarnings)
            {
                _appState.SetStatus($"Integration warning: {warning}", true);
            }

            await Load();
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
