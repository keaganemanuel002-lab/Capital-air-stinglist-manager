using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ScheduleRow : ObservableObject
{
    public int JobCardId { get; set; }
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

    public ObservableCollection<ScheduleRow> Rows { get; } = new();

    [ObservableProperty] private ScheduleRow? selectedRow;

    // Quick filters
    [ObservableProperty] private bool showOpenOnly = true;
    [ObservableProperty] private bool showTodayOnly = false;

    public InstallationsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        Load();
    }

    partial void OnShowOpenOnlyChanged(bool value) => Load();
    partial void OnShowTodayOnlyChanged(bool value) => Load();

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();

        var q = db.JobCards.AsQueryable();

        if (ShowOpenOnly)
            q = q.Where(j => j.Status == JobStatus.Open);

        if (ShowTodayOnly)
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            q = q.Where(j => j.ScheduledFor != null && j.ScheduledFor >= today && j.ScheduledFor < tomorrow);
        }

        var items = q
            .OrderBy(j => j.ScheduledFor == null) // scheduled first
            .ThenBy(j => j.ScheduledFor)
            .ThenByDescending(j => j.CreatedAt)
            .ToList();

        Rows.Clear();
        foreach (var j in items)
        {
            Rows.Add(new ScheduleRow
            {
                JobCardId = j.Id,
                Type = j.Type.ToString(),
                Status = j.Status.ToString(),
                Company = j.Company,
                Registration = j.Registration,
                ScheduledFor = j.ScheduledFor?.ToString("yyyy-MM-dd HH:mm") ?? ""
            });
        }

        _appState.SetStatus($"Loaded {Rows.Count} scheduled jobs.");
    }

    [RelayCommand]
    private async Task SetSchedule()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.ScheduleEditWindow();
        dlg.DataContext = new ScheduleEditViewModel(SelectedRow.JobCardId, () => dlg.Close());

        await dlg.ShowDialog(_window);
        Load();
        _appState.SetStatus("Schedule updated.");
    }

    [RelayCommand]
    private void ClearSchedule()
    {
        if (SelectedRow is null) return;

        using var db = new AppDbContext();
        var job = db.JobCards.Find(SelectedRow.JobCardId);
        if (job is null) return;

        job.ScheduledFor = null;
        db.SaveChanges();

        Load();
        _appState.SetStatus("Schedule cleared.");
    }
}
