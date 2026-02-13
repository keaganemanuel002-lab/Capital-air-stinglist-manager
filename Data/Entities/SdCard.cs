using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StingListManager.Data.Entities;

public class SdCard : ObservableObject
{
    public int Id { get; set; }

    private int _slotNumber = 1;
    public int SlotNumber
    {
        get => _slotNumber;
        set => SetProperty(ref _slotNumber, value);
    }

    private string? _serialNumber;
    public string? SerialNumber
    {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value);
    }

    private DateTimeOffset? _installedAt;
    public DateTimeOffset? InstalledAt
    {
        get => _installedAt;
        set => SetProperty(ref _installedAt, value);
    }

    private DateTimeOffset? _changedAt;
    public DateTimeOffset? ChangedAt
    {
        get => _changedAt;
        set => SetProperty(ref _changedAt, value);
    }

    // Vehicle or dashcam association
    public int? DashcamId { get; set; }
    public Dashcam? Dashcam { get; set; }

    private string? _installedInVehicleRegistration;
    public string? InstalledInVehicleRegistration
    {
        get => _installedInVehicleRegistration;
        set => SetProperty(ref _installedInVehicleRegistration, value);
    }

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }
}
