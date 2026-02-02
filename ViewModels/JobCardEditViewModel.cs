using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class JobCardEditViewModel : ViewModelBase
{
    private readonly int _jobCardId;
    private readonly Action _close;
    private readonly VehicleDataService _vehicleService = new();
    private bool _suppressMakeFilter;
    private bool _suppressModelFilter;

    [ObservableProperty] private string company = "";
    [ObservableProperty] private string registration = "";
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
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isFetching;
    [ObservableProperty] private bool showMakesList;
    [ObservableProperty] private bool showModelsList;

    public ObservableCollection<string> AvailableMakes { get; } = new();
    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> FilteredMakes { get; } = new();
    public ObservableCollection<string> FilteredModels { get; } = new();

    partial void OnMakeChanged(string? value)
    {
        // When make changes, update available models and clear selected model if make is different
        if (!_suppressModelFilter)
        {
            Model = null;
        }
        UpdateAvailableModels();
        FilterModels(Model);
    }

    public void FilterMakes(string? searchText)
    {
        if (_suppressMakeFilter)
        {
            _suppressMakeFilter = false;
            return;
        }

        if (string.Equals(searchText, Make, StringComparison.OrdinalIgnoreCase))
        {
            ShowMakesList = false;
            return;
        }

        FilteredMakes.Clear();
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
        {
            ShowMakesList = false;
            return;
        }
        
        var search = searchText.ToLowerInvariant();
        foreach (var make in AvailableMakes.Where(m => m.ToLowerInvariant().Contains(search)))
        {
            FilteredMakes.Add(make);
        }
        ShowMakesList = FilteredMakes.Count > 0;
    }

    public void FilterModels(string? searchText)
    {
        if (_suppressModelFilter)
        {
            _suppressModelFilter = false;
            return;
        }

        if (string.Equals(searchText, Model, StringComparison.OrdinalIgnoreCase))
        {
            ShowModelsList = false;
            return;
        }

        FilteredModels.Clear();
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
        {
            ShowModelsList = false;
            return;
        }
        
        var search = searchText.ToLowerInvariant();
        foreach (var model in AvailableModels.Where(m => m.ToLowerInvariant().Contains(search)))
        {
            FilteredModels.Add(model);
        }
        ShowModelsList = FilteredModels.Count > 0;
    }

    public void SelectMake(string value)
    {
        _suppressMakeFilter = true;
        _suppressModelFilter = true;
        Make = value;
        Model = null;
        ShowMakesList = false;
    }

    public void SelectModel(string value)
    {
        _suppressModelFilter = true;
        Model = value;
        ShowModelsList = false;
    }

    public JobCardEditViewModel(int jobCardId, Action close)
    {
        _jobCardId = jobCardId;
        _close = close;

        // Load all available makes
        RefreshAvailableMakes();

        using var db = new AppDbContext();
        var job = db.JobCards.Find(jobCardId);
        
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sting_debug.log");
        
        if (job != null)
        {
            var logMsg = $"[JobCardEditViewModel] Loaded JobCard {jobCardId}: Make={job.Make}, Model={job.Model}, Imei={job.Imei}, Iccid={job.Iccid}, SerialNumber={job.SerialNumber}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
            
            Company = job.Company;
            Registration = job.Registration;
            FleetNumber = job.FleetNumber;
            Make = job.Make;
            Model = job.Model;
            Colour = job.Colour;
            VinNumber = job.VinNumber;
            TrackingUnitMake = job.TrackingUnitMake;
            Imei = job.Imei;
            SerialNumber = job.SerialNumber;
            Iccid = job.Iccid;
            SimNumber = job.SimNumber;

            logMsg = $"[JobCardEditViewModel] Properties set: Make={Make}, Model={Model}, Imei={Imei}, Iccid={Iccid}";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);

            // Load models for the selected make
            if (!string.IsNullOrWhiteSpace(Make))
            {
                UpdateAvailableModels();
            }
        }
        else
        {
            var logMsg = $"[JobCardEditViewModel] JobCard {jobCardId} not found!";
            System.IO.File.AppendAllText(logPath, logMsg + Environment.NewLine);
        }
    }

    private void RefreshAvailableMakes()
    {
        AvailableMakes.Clear();
        foreach (var make in _vehicleService.GetAllVehicleMakes())
        {
            AvailableMakes.Add(make);
        }
    }

    private void UpdateAvailableModels()
    {
        AvailableModels.Clear();
        if (!string.IsNullOrWhiteSpace(Make))
        {
            foreach (var model in _vehicleService.GetVehicleModelsByMake(Make))
            {
                AvailableModels.Add(model);
            }
        }
    }

    [RelayCommand]
    private async Task FetchFromTeltonika()
    {
        StatusMessage = null;
        IsFetching = true;

        try
        {
            var service = new TeltonikaFotaService();
            
            if (!service.IsConfigured())
            {
                StatusMessage = "Teltonika API key not configured. Please set it in Settings.";
                return;
            }

            TeltonikaDeviceInfo? deviceInfo = null;

            // Try to fetch by IMEI first if we have it
            if (!string.IsNullOrWhiteSpace(Imei))
            {
                deviceInfo = await service.GetDeviceInfoAsync(Imei.Trim());
            }
            // Otherwise try by serial number
            else if (!string.IsNullOrWhiteSpace(SerialNumber))
            {
                deviceInfo = await service.GetDeviceInfoAsync(SerialNumber.Trim());
            }

            if (deviceInfo != null)
            {
                Imei = deviceInfo.Imei;
                SerialNumber = deviceInfo.SerialNumber;
                Iccid = deviceInfo.Iccid;
                StatusMessage = "✓ Device information fetched from Teltonika FOTA";
            }
            else
            {
                StatusMessage = "Device not found in Teltonika FOTA. Try entering IMEI or Serial Number manually.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _close();

    [RelayCommand]
    private void Save()
    {
        using var db = new AppDbContext();
        var job = db.JobCards.Find(_jobCardId);
        if (job == null) { _close(); return; }

        job.Registration = string.IsNullOrWhiteSpace(Registration) ? "" : Registration.Trim().ToUpperInvariant();
        job.FleetNumber = string.IsNullOrWhiteSpace(FleetNumber) ? null : FleetNumber.Trim();
        job.Make = string.IsNullOrWhiteSpace(Make) ? null : Make.Trim();
        job.Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim();
        job.Colour = string.IsNullOrWhiteSpace(Colour) ? null : Colour.Trim();
        job.VinNumber = string.IsNullOrWhiteSpace(VinNumber) ? null : VinNumber.Trim();
        job.TrackingUnitMake = string.IsNullOrWhiteSpace(TrackingUnitMake) ? null : TrackingUnitMake.Trim();
        job.Imei = string.IsNullOrWhiteSpace(Imei) ? null : Imei.Trim();
        job.SerialNumber = string.IsNullOrWhiteSpace(SerialNumber) ? null : SerialNumber.Trim();
        job.Iccid = string.IsNullOrWhiteSpace(Iccid) ? null : Iccid.Trim();
        job.SimNumber = string.IsNullOrWhiteSpace(SimNumber) ? null : SimNumber.Trim();
        db.SaveChanges();

        _close();
    }
}
