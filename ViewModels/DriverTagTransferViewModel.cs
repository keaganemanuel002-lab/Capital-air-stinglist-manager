using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class DriverTagTransferViewModel : ViewModelBase
{
    private readonly int _driverTagId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    public string CurrentDriverName { get; }

    [ObservableProperty] private string toDriverName = string.Empty;
    [ObservableProperty] private string transferReason = string.Empty;
    [ObservableProperty] private DateTimeOffset? transferredAt = DateTimeOffset.Now.Date;
    [ObservableProperty] private string? errorMessage;

    public DriverTagTransferViewModel(
        int driverTagId,
        string currentDriverName,
        Action close,
        Action onSaved,
        AppState appState)
    {
        _driverTagId = driverTagId;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        CurrentDriverName = string.IsNullOrWhiteSpace(currentDriverName) ? "-" : currentDriverName;
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

        var targetDriver = NormalizeSingleLine(ToDriverName);
        var reason = NormalizeSingleLine(TransferReason);
        if (string.IsNullOrWhiteSpace(targetDriver))
        {
            ErrorMessage = "New driver name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "Transfer reason is required.";
            return;
        }

        if (TransferredAt is null)
        {
            ErrorMessage = "Transfer date is required.";
            return;
        }

        using var db = new AppDbContext();
        var record = db.DriverTags.FirstOrDefault(x => x.Id == _driverTagId);
        if (record is null)
        {
            ErrorMessage = "Driver tag record not found.";
            return;
        }

        if (record.EmploymentExitType != DriverEmploymentExitType.None)
        {
            ErrorMessage = "Cannot transfer a tag already marked as resigned/fired.";
            return;
        }

        if (record.LostOrDamagedReportedAt is not null)
        {
            ErrorMessage = "Cannot transfer a tag that is marked lost/damaged.";
            return;
        }

        if (string.Equals(record.DriverName, targetDriver, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "New driver must be different from current driver.";
            return;
        }

        var transfer = new DriverTagTransfer
        {
            DriverTagId = record.Id,
            FromDriverName = record.DriverName,
            ToDriverName = targetDriver,
            Reason = reason,
            TransferredAt = TransferredAt.Value.UtcDateTime,
            TransferredBy = _appState.OperatorName
        };
        db.DriverTagTransfers.Add(transfer);

        record.DriverName = targetDriver;
        record.EmploymentExitType = DriverEmploymentExitType.None;
        record.EmploymentExitAt = null;
        record.ReturnStatus = DriverTagReturnStatus.Unknown;
        record.ReturnedAt = null;

        db.SaveChanges();

        _appState.SetStatus($"Driver tag {record.TagCode} transferred to {record.DriverName}.");
        _onSaved();
        _close();
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
