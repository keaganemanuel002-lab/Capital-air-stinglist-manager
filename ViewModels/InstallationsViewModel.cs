using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ScheduleRow : ObservableObject
{
    public int JobCardId { get; set; }
    public int JobCardNumber { get; set; }
    public string JobCardReference { get; set; } = "";
    public string QuoteReference { get; set; } = "-";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string ScheduledFor { get; set; } = "";
}

public partial class InstallationsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly IDataStore _dataStore;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<ScheduleRow> Rows { get; } = new();

    [ObservableProperty] private ScheduleRow? selectedRow;

    // Quick filters
    [ObservableProperty] private bool showOpenOnly = true;
    [ObservableProperty] private bool showTodayOnly = false;

    public InstallationsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        _dataStore = DataStoreFactory.Create(_appState.Settings);
        _ = Load();
    }

    partial void OnShowOpenOnlyChanged(bool value) => _ = Load();
    partial void OnShowTodayOnlyChanged(bool value) => _ = Load();

    [RelayCommand]
    private async Task Load()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var items = await _dataStore.GetJobCardsAsync(new JobCardQuery
            {
                SelectedStatus = ShowOpenOnly ? JobStatus.Open.ToString() : "All",
                SelectedType = JobType.Install.ToString(),
                CompanyFilter = null,
                RegistrationFilter = null,
                StartDate = null,
                EndDate = null,
                QuoteIdFilter = null
            }, token);

            if (ShowTodayOnly)
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                items = items.Where(j =>
                        j.ScheduledFor != null
                        && j.ScheduledFor >= today
                        && j.ScheduledFor < tomorrow)
                    .ToList();
            }

            var ordered = items
                .OrderBy(j => j.ScheduledFor == null) // scheduled first
                .ThenBy(j => j.ScheduledFor)
                .ThenByDescending(j => j.CreatedAt)
                .ToList();

            Rows.Clear();
            foreach (var j in ordered)
            {
                Rows.Add(new ScheduleRow
                {
                    JobCardId = j.Id,
                    JobCardNumber = j.JobCardNumber,
                    JobCardReference = j.JobCardReference,
                    QuoteReference = j.QuoteReference,
                    Type = j.Type,
                    Status = j.Status,
                    Company = j.Company,
                    Registration = j.Registration,
                    ScheduledFor = j.ScheduledFor?.ToString("yyyy-MM-dd HH:mm") ?? ""
                });
            }

            _appState.SetStatus($"Loaded {Rows.Count} installation job card(s).");
        }
        catch (OperationCanceledException)
        {
            // Newer load superseded this request.
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading installations: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SetSchedule()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.ScheduleEditWindow();
        dlg.DataContext = new ScheduleEditViewModel(SelectedRow.JobCardId, () => dlg.Close(), _appState);

        await dlg.ShowDialog(_window);
        await Load();
        _appState.SetStatus("Schedule updated.");
    }

    [RelayCommand]
    private async Task ClearSchedule()
    {
        if (SelectedRow is null) return;

        var updated = await _dataStore.UpdateJobCardScheduleAsync(SelectedRow.JobCardId, null);
        if (!updated)
        {
            _appState.SetStatus("Job card not found.");
            return;
        }

        await Load();
        _appState.SetStatus("Schedule cleared.");
    }
}
