using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StingListManager.Data.Entities;

public class Dashcam : ObservableObject
{
    public int Id { get; set; }

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
