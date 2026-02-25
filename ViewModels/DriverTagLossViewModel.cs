using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class DriverTagLossViewModel : ViewModelBase
{
    private readonly int _driverTagId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    public string TagCode { get; }

    [ObservableProperty] private DateTimeOffset? reportedAt = DateTimeOffset.Now.Date;
    [ObservableProperty] private string lossOrDamageReason = string.Empty;
    [ObservableProperty] private string? errorMessage;

    public DriverTagLossViewModel(
        int driverTagId,
        string tagCode,
        Action close,
        Action onSaved,
        AppState appState)
    {
        _driverTagId = driverTagId;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        TagCode = string.IsNullOrWhiteSpace(tagCode) ? "-" : tagCode;
    }

    [RelayCommand]
    private void Cancel()
    {
        _close();
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (ReportedAt is null)
        {
            ErrorMessage = "Reported date is required.";
            return;
        }

        var reason = string.IsNullOrWhiteSpace(LossOrDamageReason)
            ? string.Empty
            : LossOrDamageReason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "Reason is required (Lost or Damaged).";
            return;
        }

        using var db = new AppDbContext();
        var record = db.DriverTags.FirstOrDefault(x => x.Id == _driverTagId);
        if (record is null)
        {
            ErrorMessage = "Driver tag record not found.";
            return;
        }

        record.LostOrDamagedReportedAt = ReportedAt.Value.UtcDateTime;
        record.LostOrDamagedReason = reason;
        db.SaveChanges();

        _appState.SetStatus($"Tag {record.TagCode} marked lost/damaged.");
        _onSaved();
        _close();
    }
}
