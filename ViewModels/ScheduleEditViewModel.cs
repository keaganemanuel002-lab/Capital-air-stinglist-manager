using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;

namespace StingListManager.ViewModels;

public partial class ScheduleEditViewModel : ViewModelBase
{
    private readonly int _jobCardId;
    private readonly Action _close;

    [ObservableProperty] private DateTimeOffset? date = DateTimeOffset.Now.Date;
    [ObservableProperty] private string timeText = "08:30";
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isCancelled = true;

    public ScheduleEditViewModel(int jobCardId, Action close)
    {
        _jobCardId = jobCardId;
        _close = close;

        using var db = new AppDbContext();
        if (jobCardId > 0)
        {
            var job = db.JobCards.Find(jobCardId);
            if (job?.ScheduledFor != null)
            {
                var dt = job.ScheduledFor.Value;
                Date = new DateTimeOffset(dt.Date);
                TimeText = dt.ToString("HH:mm");
            }
        }
    }

    public DateTime? GetScheduledDateTime()
    {
        if (Date is null)
            return null;

        if (!TimeSpan.TryParse(TimeText, out var t))
            return null;

        return Date.Value.Date + t;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsCancelled = true;
        _close();
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (Date is null)
        {
            ErrorMessage = "Please select a date.";
            return;
        }

        if (!TimeSpan.TryParse(TimeText, out var t))
        {
            ErrorMessage = "Time must be HH:mm (e.g. 08:30).";
            return;
        }

        if (_jobCardId > 0)
        {
            var scheduled = Date.Value.Date + t;

            using var db = new AppDbContext();
            var job = db.JobCards.Find(_jobCardId);
            if (job is null) { _close(); return; }

            job.ScheduledFor = scheduled;
            db.SaveChanges();
        }

        IsCancelled = false;
        _close();
    }
}
