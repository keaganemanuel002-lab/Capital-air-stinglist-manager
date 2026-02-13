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

    public JobCardsViewModel(Window window, AppState appState, DateTime? startDate = null, DateTime? endDate = null)
    {
        _window = window;
        _appState = appState;
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

        var items = query
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        Rows.Clear();
        foreach (var j in items)
        {
            Rows.Add(new JobCardRow
            {
                Id = j.Id,
                JobCardNumber = j.JobCardNumber,
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
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.JobCardEditWindow();
        dlg.DataContext = new JobCardEditViewModel(SelectedRow.Id, () => dlg.Close(), _appState);

        await dlg.ShowDialog(_window);

        Load();
    }

    [RelayCommand]
    private async Task CompleteSelected()
    {
        if (!CanCompleteJobs) { _appState.SetStatus("Not permitted."); return; }
        if (SelectedRow is null) return;

        var wf = new WorkflowService();
        var result = await wf.CompleteJobCardAsync(SelectedRow.Id, _appState.OperatorName, _appState.Settings.WialonApiToken);
        _appState.SetStatus(result.message);
        Load();
    }

    [RelayCommand]
    private async Task OpenDocuments()
    {
        if (SelectedRow is null) return;

        var wnd = new StingListManager.Views.DocumentsWindow();
        var vm = new JobCardDocumentsViewModel(_window, _appState, SelectedRow.Id);

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

            var suggestedFileName = jobCards.Count == 1
                ? $"JobCard_{jobCards[0].JobCardNumber}_{jobCards[0].Registration}.pdf"
                : $"JobCards_{jobCards.Count}_items_{DateTime.Now:yyyyMMdd}.pdf";

            var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Job Card PDF",
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } }
                }
            });

            if (file is null) return;

            await File.WriteAllBytesAsync(file.Path.LocalPath, pdfBytes);

            _appState.SetStatus($"Exported {jobCards.Count} job card(s) to PDF: {Path.GetFileName(file.Path.LocalPath)}");
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error exporting PDF: {ex.Message}");
        }
    }
}
