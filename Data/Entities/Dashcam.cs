using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StingListManager.Data.Entities;

public class Dashcam : ObservableObject
{
    public int Id { get; set; }

    // Spreadsheet-aligned fields
    private string? _vehicle;
    public string? Vehicle
    {
        get => _vehicle;
        set => SetProperty(ref _vehicle, value);
    }

    private string? _deviceId;
    public string? DeviceId
    {
        get => _deviceId;
        set => SetProperty(ref _deviceId, value);
    }

    private string? _wifiPassword;
    public string? WifiPassword
    {
        get => _wifiPassword;
        set => SetProperty(ref _wifiPassword, value);
    }

    private string? _isupPassword;
    public string? IsupPassword
    {
        get => _isupPassword;
        set => SetProperty(ref _isupPassword, value);
    }

    private string? _interiorCam;
    public string? InteriorCam
    {
        get => _interiorCam;
        set => SetProperty(ref _interiorCam, value);
    }

    private string? _rearCam;
    public string? RearCam
    {
        get => _rearCam;
        set => SetProperty(ref _rearCam, value);
    }

    // Kept as text because spreadsheet contains values like
    // "27/07/2022 (Reinstalled 25/06/2025)".
    private string? _installed;
    public string? Installed
    {
        get => _installed;
        set => SetProperty(ref _installed, value);
    }

    private string? _location;
    public string? Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    private string? _issue;
    public string? Issue
    {
        get => _issue;
        set => SetProperty(ref _issue, value);
    }

    private string? _upgradeSteps;
    public string? UpgradeSteps
    {
        get => _upgradeSteps;
        set => SetProperty(ref _upgradeSteps, value);
    }

    private string? _serialNumber;
    public string? SerialNumber
    {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value);
    }

    private string? _model;
    public string? Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    private DateTimeOffset? _purchasedAt;
    public DateTimeOffset? PurchasedAt
    {
        get => _purchasedAt;
        set => SetProperty(ref _purchasedAt, value);
    }

    // Vehicle registration the dashcam is currently allocated to (if any)
    private string? _allocatedVehicleRegistration;
    public string? AllocatedVehicleRegistration
    {
        get => _allocatedVehicleRegistration;
        set => SetProperty(ref _allocatedVehicleRegistration, value);
    }

    // Historical transfer info
    private string? _transferredFromRegistration;
    public string? TransferredFromRegistration
    {
        get => _transferredFromRegistration;
        set => SetProperty(ref _transferredFromRegistration, value);
    }

    private string? _transferredToRegistration;
    public string? TransferredToRegistration
    {
        get => _transferredToRegistration;
        set => SetProperty(ref _transferredToRegistration, value);
    }

    private DateTimeOffset? _transferredAt;
    public DateTimeOffset? TransferredAt
    {
        get => _transferredAt;
        set => SetProperty(ref _transferredAt, value);
    }

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public List<SdCard> SdCards { get; set; } = new();
}
