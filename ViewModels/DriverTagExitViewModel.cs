using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class DriverTagExitViewModel : ViewModelBase
{
    private readonly int _driverTagId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    public string TagCode { get; }
    public string DriverName { get; }

    public ObservableCollection<string> ExitTypeOptions { get; } = new() { "Resigned", "Fired" };
    public ObservableCollection<string> ReturnOptions { get; } = new() { "Returned", "Not Returned" };

    [ObservableProperty] private string selectedExitType = "Resigned";
    [ObservableProperty] private string selectedReturnStatus = "Returned";
    [ObservableProperty] private DateTimeOffset? exitDate = DateTimeOffset.Now.Date;
    [ObservableProperty] private DateTimeOffset? returnedAt = DateTimeOffset.Now.Date;
    [ObservableProperty] private string? errorMessage;

    public bool IsReturnedSelected => string.Equals(SelectedReturnStatus, "Returned", StringComparison.OrdinalIgnoreCase);

    public DriverTagExitViewModel(
        int driverTagId,
        string tagCode,
        string driverName,
        Action close,
        Action onSaved,
        AppState appState)
    {
        _driverTagId = driverTagId;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        TagCode = string.IsNullOrWhiteSpace(tagCode) ? "-" : tagCode;
        DriverName = string.IsNullOrWhiteSpace(driverName) ? "-" : driverName;
    }

    partial void OnSelectedReturnStatusChanged(string value)
    {
        if (!IsReturnedSelected)
            ReturnedAt = null;
        else if (ReturnedAt is null)
            ReturnedAt = DateTimeOffset.Now.Date;

        OnPropertyChanged(nameof(IsReturnedSelected));
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

        if (ExitDate is null)
        {
            ErrorMessage = "Exit date is required.";
            return;
        }

        if (IsReturnedSelected && ReturnedAt is null)
        {
            ErrorMessage = "Returned date is required when return status is Returned.";
            return;
        }

        if (IsReturnedSelected && ReturnedAt!.Value.Date < ExitDate.Value.Date)
        {
            ErrorMessage = "Returned date cannot be earlier than exit date.";
            return;
        }

        using var db = new AppDbContext();
        var record = db.DriverTags.FirstOrDefault(x => x.Id == _driverTagId);
        if (record is null)
        {
            ErrorMessage = "Driver tag record not found.";
            return;
        }

        record.EmploymentExitType = SelectedExitType switch
        {
            "Fired" => DriverEmploymentExitType.Fired,
            _ => DriverEmploymentExitType.Resigned
        };
        record.EmploymentExitAt = ExitDate.Value.UtcDateTime;
        record.ReturnStatus = IsReturnedSelected
            ? DriverTagReturnStatus.Returned
            : DriverTagReturnStatus.NotReturned;
        record.ReturnedAt = IsReturnedSelected ? ReturnedAt?.UtcDateTime : null;

        db.SaveChanges();

        _appState.SetStatus($"Recorded {record.EmploymentExitType} for {record.DriverName} ({record.TagCode}).");
        _onSaved();
        _close();
    }
}
