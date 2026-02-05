using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class WialonReportRow : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    
    [ObservableProperty]
    private string _location = "";
    
    public DateTime? LastUpdateAt { get; set; }
    public string Status { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public partial class WialonReportsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private WialonApiService? _wialonService;
    private List<WialonReportRow> _allReports = new();
    private static List<WialonReportRow> _cachedReports = new();  // Persist across page navigations
    private static Task? _backgroundGeocodeTask;  // Continue geocoding even when navigating away

    public ObservableCollection<WialonReportRow> Reports { get; } = new();
    public ObservableCollection<string> AvailableClients { get; } = new();

    [ObservableProperty] private int progressCount;
    [ObservableProperty] private int progressTotal;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string wialonToken = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? selectedReportType;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;
    [ObservableProperty] private string? selectedClient;

    partial void OnSelectedClientChanged(string? value)
    {
        FilterReports();
    }

    public static readonly string[] ReportTypes = new[] 
    { 
        "Trips",
        "Stops", 
        "Mileage",
        "Driver Behavior",
        "Fuel",
        "Detections"
    };

    public WialonReportsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        
        // Set default dates to current month
        var today = DateTime.Today;
        StartDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        EndDate = new DateTimeOffset(new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));

        // Restore cached reports if available
        if (_cachedReports.Count > 0)
        {
            _allReports = new List<WialonReportRow>(_cachedReports);
            
            // Rebuild client list
            AvailableClients.Clear();
            AvailableClients.Add("All Clients");
            
            var uniqueClients = _cachedReports
                .Select(r => r.Client)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c);
            
            foreach (var client in uniqueClients)
            {
                AvailableClients.Add(client);
            }
            
            // Set default filter and restore view
            if (string.IsNullOrEmpty(SelectedClient))
                SelectedClient = "All Clients";
            
            FilterReports();
            IsConnected = true;  // Mark as connected if we have cached data
        }

        // Load saved Wialon token and auto-connect
        if (!string.IsNullOrWhiteSpace(_appState.Settings.WialonApiToken))
        {
            WialonToken = _appState.Settings.WialonApiToken;
            _ = ConnectToWialon(); // Fire and forget auto-connect
        }
    }

    [RelayCommand]
    private async Task ConnectToWialon()
    {
        if (string.IsNullOrWhiteSpace(WialonToken))
        {
            _appState.SetStatus("Please enter a Wialon API token.");
            return;
        }

        try
        {
            IsLoading = true;
            _wialonService = new WialonApiService(WialonToken);
            
            var isConnected = await _wialonService.TestConnectionAsync();
            
            if (isConnected)
            {
                IsConnected = true;
                
                // Save token to settings
                _appState.Settings.WialonApiToken = WialonToken;
                _appState.SaveSettings();
                
                _appState.SetStatus("Successfully connected to Wialon API.");
                await LoadReports();
            }
            else
            {
                IsConnected = false;
                var errorMsg = _wialonService.LastError ?? "Unknown error. Please check your token.";
                _appState.SetStatus($"Failed to connect to Wialon API: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _appState.SetStatus($"Connection error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadReports()
    {
        if (_wialonService is null)
        {
            _appState.SetStatus("Not connected to Wialon. Please connect first.");
            return;
        }

        try
        {
            IsLoading = true;
            ProgressCount = 0;
            ProgressTotal = 0;
            
            _allReports.Clear();
            _cachedReports.Clear();
            Reports.Clear();
            AvailableClients.Clear();
            
            // Add "All Clients" option
            AvailableClients.Add("All Clients");
            
            // First batch to get total count
            var (firstBatch, totalCount) = await _wialonService.GetReportsAsync(0, 100);
            ProgressTotal = totalCount;
            ProgressCount = firstBatch.Count;
            
            // Add first batch
            await AddReportsToList(firstBatch);
            
            // Load remaining batches
            if (totalCount > 100)
            {
                for (int from = 100; from < totalCount; from += 100)
                {
                    IsLoadingMore = true;
                    var (batch, _) = await _wialonService.GetReportsAsync(from, 100);
                    ProgressCount += batch.Count;
                    await AddReportsToList(batch);
                    IsLoadingMore = false;
                    
                    // Small delay to avoid overwhelming the API
                    await Task.Delay(50);
                }
            }
            
            // Setup client list and filter
            var uniqueClients = _allReports
                .Select(r => r.Client)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c);
            
            foreach (var client in uniqueClients)
            {
                AvailableClients.Add(client);
            }
            
            // Cache for persistence
            _cachedReports = new List<WialonReportRow>(_allReports);
            
            // Set default filter and display all
            if (string.IsNullOrEmpty(SelectedClient))
                SelectedClient = "All Clients";
            
            FilterReports();
            
            _appState.SetStatus($"Loaded all {_allReports.Count} vehicles from Wialon. Geocoding addresses in background...");
            
            // Start background geocoding
            _backgroundGeocodeTask = GeocodeAddressesInBackgroundAsync(_allReports);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error loading reports: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
        }
    }

    private async Task AddReportsToList(List<StingListManager.Services.WialonReport> reports)
    {
        foreach (var report in reports)
        {
            var row = new WialonReportRow
            {
                Id = report.Id,
                Name = report.Name,
                Client = report.Client,
                CreatedAt = report.CreatedAt,
                Location = report.Location,
                LastUpdateAt = report.LastUpdateAt,
                Status = report.Status,
                Latitude = report.Latitude,
                Longitude = report.Longitude
            };
            _allReports.Add(row);
            
            // Add to display if "All Clients" is selected
            if (SelectedClient == "All Clients" || SelectedClient == report.Client)
            {
                Reports.Add(row);
            }
        }
    }

    private async Task GeocodeAddressesInBackgroundAsync(List<WialonReportRow> reportRows)
    {
        if (_wialonService is null)
            return;

        try
        {
            // Collect all reports that need geocoding
            var toGeocode = reportRows.Where(r => 
                r.Latitude.HasValue && r.Longitude.HasValue && 
                (r.Location == "Loading address..." || r.Location == "Loading..." || r.Location == "Unknown")).ToList();

            if (toGeocode.Count == 0)
                return;

            System.Diagnostics.Debug.WriteLine($"Starting background geocoding for {toGeocode.Count} addresses");

            foreach (var row in toGeocode)
            {
                try
                {
                    var address = await _wialonService.ResolveAddressAsync(row.Latitude!.Value, row.Longitude!.Value);
                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        row.Location = address;
                        System.Diagnostics.Debug.WriteLine($"Geocoded {row.Name}: {address}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Geocoding failed for {row.Name}: {ex.Message}");
                }

                // Small delay to avoid overloading the API
                await Task.Delay(100);
            }

            System.Diagnostics.Debug.WriteLine($"Background geocoding completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Background geocoding error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task GenerateReport()
    {
        if (_wialonService is null)
        {
            _appState.SetStatus("Not connected to Wialon. Please connect first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedReportType))
        {
            _appState.SetStatus("Please select a report type.");
            return;
        }

        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            _appState.SetStatus("Please select start and end dates.");
            return;
        }

        try
        {
            IsLoading = true;
            var startDate = StartDate.Value.DateTime;
            var endDate = EndDate.Value.DateTime;
            
            var result = await _wialonService.GenerateReportAsync(SelectedReportType, startDate, endDate);
            
            _appState.SetStatus($"Report generated: {result}");
            await LoadReports();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error generating report: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        _wialonService?.Dispose();
        _wialonService = null;
        IsConnected = false;
        WialonToken = "";
        Reports.Clear();
        _allReports.Clear();
        _cachedReports.Clear();  // Clear cache on disconnect
        AvailableClients.Clear();
        
        // Clear saved token from settings
        _appState.Settings.WialonApiToken = null;
        _appState.SaveSettings();
        
        _appState.SetStatus("Disconnected from Wialon.");
    }

    private void FilterReports()
    {
        Reports.Clear();
        
        var filtered = _allReports.AsEnumerable();
        
        // Filter by client
        if (!string.IsNullOrEmpty(SelectedClient) && SelectedClient != "All Clients")
        {
            filtered = filtered.Where(r => r.Client == SelectedClient);
        }
        
        foreach (var report in filtered)
        {
            Reports.Add(report);
        }
    }
}
