using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ScheduleEditViewModel : ViewModelBase
{
    private readonly int _jobCardId;
    private readonly Action _close;
    private readonly IDataStore _dataStore;

    [ObservableProperty] private DateTimeOffset? date = DateTimeOffset.Now.Date;
    [ObservableProperty] private string timeText = "08:30";
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isCancelled = true;

    public ScheduleEditViewModel(int jobCardId, Action close, AppState appState)
    {
        _jobCardId = jobCardId;
        _close = close;
        _dataStore = DataStoreFactory.Create(appState.Settings);
        _ = LoadExistingScheduleAsync();
    }

    private async Task LoadExistingScheduleAsync()
    {
        if (_jobCardId <= 0)
            return;

        try
        {
            var scheduled = await _dataStore.GetJobCardScheduledForAsync(_jobCardId);
            if (scheduled is null)
                return;

            var dt = scheduled.Value;
            Date = new DateTimeOffset(dt.Date);
            TimeText = dt.ToString("HH:mm");
        }
        catch
        {
            // Keep default schedule fields if read fails.
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
    private async Task Save()
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
            var updated = await _dataStore.UpdateJobCardScheduleAsync(_jobCardId, scheduled);
            if (!updated)
            {
                ErrorMessage = "Job card not found.";
                return;
            }
        }

        IsCancelled = false;
        _close();
    }
}
