using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class BillingEntryEditViewModel : ViewModelBase
{
    private readonly int _billingEntryId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    [ObservableProperty] private string windowTitle = "Edit Billing Entry";
    [ObservableProperty] private string company = string.Empty;
    [ObservableProperty] private string registration = string.Empty;
    [ObservableProperty] private string? fleetNumber;
    [ObservableProperty] private string? make;
    [ObservableProperty] private string? model;
    [ObservableProperty] private string? colour;
    [ObservableProperty] private string? vinNumber;
    [ObservableProperty] private string? trackingUnitMake;
    [ObservableProperty] private string? imei;
    [ObservableProperty] private string? serialNumber;
    [ObservableProperty] private string? iccid;
    [ObservableProperty] private string? simNumber;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? reason;
    [ObservableProperty] private string? errorMessage;

    public BillingEntryEditViewModel(int billingEntryId, Action close, Action onSaved, AppState appState)
    {
        _billingEntryId = billingEntryId;
        _close = close;
        _onSaved = onSaved;
        _appState = appState;

        Load();
    }

    private void Load()
    {
        using var db = new AppDbContext();
        var entry = db.BillingEntries.AsNoTracking().FirstOrDefault(x => x.Id == _billingEntryId);
        if (entry is null)
        {
            ErrorMessage = "Billing entry not found.";
            return;
        }

        Company = entry.Company;
        Registration = entry.Registration;
        FleetNumber = entry.FleetNumber;
        Make = entry.Make;
        Model = entry.Model;
        Colour = entry.Colour;
        VinNumber = entry.VinNumber;
        TrackingUnitMake = entry.TrackingUnitMake;
        Imei = entry.Imei;
        SerialNumber = entry.SerialNumber;
        Iccid = entry.Iccid;
        SimNumber = entry.SimNumber;
        Notes = entry.Notes;
        Reason = entry.Reason;
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

        var normalizedCompany = NormalizeText(Company);
        var normalizedRegistration = NormalizeText(Registration).ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCompany))
        {
            ErrorMessage = "Company is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedRegistration))
        {
            ErrorMessage = "Registration is required.";
            return;
        }

        using var db = new AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(x => x.Id == _billingEntryId);
        if (entry is null)
        {
            ErrorMessage = "Billing entry no longer exists.";
            return;
        }

        entry.Company = normalizedCompany;
        entry.Registration = normalizedRegistration;
        entry.FleetNumber = TrimOrNull(FleetNumber);
        entry.Make = TrimOrNull(Make);
        entry.Model = TrimOrNull(Model);
        entry.Colour = TrimOrNull(Colour);
        entry.VinNumber = TrimOrNull(VinNumber);
        entry.Imei = TrimOrNull(Imei);
        entry.SerialNumber = TrimOrNull(SerialNumber);
        entry.Iccid = TrimOrNull(Iccid);
        entry.SimNumber = TrimOrNull(SimNumber);
        entry.Notes = TrimOrNull(Notes);
        entry.Reason = TrimOrNull(Reason);

        var rawUnit = TrimOrNull(TrackingUnitMake);
        entry.TrackingUnitMake = StingPackageClassifier.NormalizeLabel(rawUnit) ?? rawUnit;

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "Could not save. Another active entry already uses this registration/IMEI/ICCID/serial.";
            return;
        }

        _appState.SetStatus($"Billing entry updated: {entry.Company} / {entry.Registration}");
        _onSaved();
        _close();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
