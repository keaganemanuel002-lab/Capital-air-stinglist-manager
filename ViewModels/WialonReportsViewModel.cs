using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClosedXML.Excel;
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
    
    private DateTime? _lastUpdateAt;
    public DateTime? LastUpdateAt 
    { 
        get => _lastUpdateAt;
        set
        {
            if (SetProperty(ref _lastUpdateAt, value))
            {
                // Recalculate communication status when LastUpdateAt changes
                OnPropertyChanged(nameof(CommunicationStatus));
                OnPropertyChanged(nameof(CommunicationStatusColor));
                OnPropertyChanged(nameof(CommunicationStatusBrush));
            }
        }
    }

    public string Status { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Computed properties for communication status
    public string CommunicationStatus
    {
        get
        {
            if (!LastUpdateAt.HasValue)
                return "Unknown";

            var daysSinceUpdate = (DateTime.Now - LastUpdateAt.Value).TotalDays;

            if (daysSinceUpdate <= 2)
                return "Updating";
            else if (daysSinceUpdate <= 13)
                return "Uncommunicative (<14d)";
            else
                return "Uncommunicative (>14d)";
        }
    }

    public Color CommunicationStatusColor
    {
        get
        {
            if (!LastUpdateAt.HasValue)
                return Color.Parse("#CCCCCC"); // Gray for unknown

            var daysSinceUpdate = (DateTime.Now - LastUpdateAt.Value).TotalDays;

            if (daysSinceUpdate <= 2)
                return Color.Parse("#22AB94"); // Green - updating
            else if (daysSinceUpdate <= 13)
                return Color.Parse("#FFA500"); // Yellow/Orange - uncommunicative <14d
            else
                return Color.Parse("#FF6B6B"); // Orange/Red - uncommunicative >14d
        }
    }

    public SolidColorBrush CommunicationStatusBrush
    {
        get
        {
            if (!LastUpdateAt.HasValue)
                return new SolidColorBrush(Color.Parse("#CCCCCC")); // Gray for unknown

            var daysSinceUpdate = (DateTime.Now - LastUpdateAt.Value).TotalDays;

            if (daysSinceUpdate <= 2)
                return new SolidColorBrush(Color.Parse("#22AB94")); // Green - updating
            else if (daysSinceUpdate <= 13)
                return new SolidColorBrush(Color.Parse("#FFA500")); // Yellow/Orange - uncommunicative <14d
            else
                return new SolidColorBrush(Color.Parse("#FF6B6B")); // Orange/Red - uncommunicative >14d
        }
    }
}

public partial class WialonReportsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private WialonApiService? _wialonService;
    private List<WialonReportRow> _allReports = new();
    private static List<WialonReportRow> _cachedReports = new();  // Persist across page navigations
    private readonly Dictionary<string, int> _clientIds = new();
    private bool _suppressClientChange;

    public ObservableCollection<WialonReportRow> Reports { get; } = new();
    public ObservableCollection<string> AvailableClients { get; } = new();

    [ObservableProperty] private int progressCount;
    [ObservableProperty] private int progressTotal;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private int geocodingProgress;
    [ObservableProperty] private int geocodingTotal;
    [ObservableProperty] private bool isGeocoding;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string wialonToken = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? selectedReportType;
    [ObservableProperty] private DateTimeOffset? startDate;
    [ObservableProperty] private DateTimeOffset? endDate;
    [ObservableProperty] private string? selectedClient;

    partial void OnSelectedClientChanged(string? value)
    {
        if (_suppressClientChange)
        {
            return;
        }

        if (IsConnected && !string.IsNullOrWhiteSpace(value) && value != "All Clients")
        {
            _ = LoadReports();
        }
        else
        {
            Reports.Clear();
        }
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
                await LoadClientsAsync();

                if (!string.IsNullOrWhiteSpace(SelectedClient) && SelectedClient != "All Clients")
                {
                    await LoadReports();
                }
                else
                {
                    _appState.SetStatus("Select a client, then click Refresh to load vehicles.");
                }
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

        if (string.IsNullOrWhiteSpace(SelectedClient) || SelectedClient == "All Clients")
        {
            _appState.SetStatus("Please select a client to load vehicles.");
            return;
        }

        if (!_clientIds.TryGetValue(SelectedClient, out var accountId))
        {
            await LoadClientsAsync();
            if (!_clientIds.TryGetValue(SelectedClient, out accountId))
            {
                _appState.SetStatus("Selected client not found. Please refresh the client list.");
                return;
            }
        }

        try
        {
            IsLoading = true;
            ProgressCount = 0;
            ProgressTotal = 0;
            
            _allReports.Clear();
            _cachedReports.Clear();
            Reports.Clear();
            
            // First batch to get total count (try billing account name, then creator name, then creator ID)
            var searchAttempts = new List<(string PropName, string PropValue)>
            {
                ("rel_billing_account_name", SelectedClient),
                ("rel_user_creator_name", SelectedClient)
            };

            if (accountId > 0)
            {
                searchAttempts.Add(("sys_user_creator", accountId.ToString()));
            }

            var (firstBatch, totalCount) = await _wialonService.GetReportsAsync(0, 100, searchAttempts[0].PropName, searchAttempts[0].PropValue);
            var attemptIndex = 0;
            while (totalCount == 0 && attemptIndex + 1 < searchAttempts.Count)
            {
                attemptIndex++;
                (firstBatch, totalCount) = await _wialonService.GetReportsAsync(0, 100, searchAttempts[attemptIndex].PropName, searchAttempts[attemptIndex].PropValue);
            }

            if (totalCount == 0)
            {
                _appState.SetStatus($"No vehicles found for {SelectedClient}. Check the client name or permissions.");
                return;
            }
            ProgressTotal = totalCount;
            ProgressCount = firstBatch.Count;
            
            // Add first batch
            AddReportsToList(firstBatch);
            
            // Load remaining batches
            if (totalCount > 100)
            {
                for (int from = 100; from < totalCount; from += 100)
                {
                    IsLoadingMore = true;
                    var (batch, _) = await _wialonService.GetReportsAsync(from, 100, searchAttempts[attemptIndex].PropName, searchAttempts[attemptIndex].PropValue);
                    ProgressCount += batch.Count;
                    AddReportsToList(batch);
                    IsLoadingMore = false;
                    
                    // Small delay to avoid overwhelming the API
                    await Task.Delay(50);
                }
            }
            
            // Cache for persistence
            _cachedReports = new List<WialonReportRow>(_allReports);
            
            FilterReports();
            
            _appState.SetStatus($"Loaded {_allReports.Count} vehicles for {SelectedClient}. Geocoding addresses in background...");
            Console.WriteLine($"[LOAD] Starting background geocoding for {Reports.Count} vehicles");
            Console.WriteLine($"[LOAD] Address sample: {(Reports.FirstOrDefault()?.Location ?? "none")}");
            
            // Start background geocoding - only geocode the selected client's vehicles
            await GeocodeAddressesInBackgroundAsync(Reports.ToList());
            
            Console.WriteLine($"[LOAD] Geocoding completed");
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

    private void AddReportsToList(List<StingListManager.Services.WialonReport> reports)
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
            Reports.Add(row);
        }
    }

    private async Task LoadClientsAsync()
    {
        if (_wialonService is null)
        {
            return;
        }

        try
        {
            var clients = await _wialonService.GetResourcesAsync();

            _suppressClientChange = true;
            _clientIds.Clear();
            AvailableClients.Clear();
            AvailableClients.Add("All Clients");

            foreach (var client in clients.OrderBy(c => c.Value))
            {
                AvailableClients.Add(client.Value);
                if (!_clientIds.ContainsKey(client.Value))
                {
                    _clientIds[client.Value] = client.Key;
                }
            }

            if (string.IsNullOrEmpty(SelectedClient))
            {
                SelectedClient = "All Clients";
            }
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Failed to load clients: {ex.Message}");
        }
        finally
        {
            _suppressClientChange = false;
        }
    }

    private async Task GeocodeAddressesInBackgroundAsync(List<WialonReportRow> reportRows)
    {
        if (_wialonService is null)
            return;

        try
        {
            // Collect all reports that need geocoding
            // Geocode if we have coordinates and the location is either Unknown or looks like coordinates
            var toGeocode = reportRows.Where(r => 
                r.Latitude.HasValue && r.Longitude.HasValue && 
                (r.Location == "Unknown" || 
                 r.Location.Contains(",") ||  // Matches coordinate format like "33.8753, 18.4927"
                 r.Location == "Loading address..." || 
                 r.Location == "Loading...")).ToList();

            if (toGeocode.Count == 0)
            {
                Console.WriteLine($"[BATCH] No addresses to geocode");
                return;
            }

            Console.WriteLine($"[BATCH] Starting geocoding for {toGeocode.Count} addresses");

            // Initialize progress tracking
            GeocodingTotal = toGeocode.Count;
            GeocodingProgress = 0;
            IsGeocoding = true;

            // Process addresses in smaller batches with delays to avoid rate limiting
            var batchSize = 10;
            var currentBatch = 0;
            var totalBatches = (toGeocode.Count + batchSize - 1) / batchSize;

            for (int i = 0; i < toGeocode.Count; i += batchSize)
            {
                currentBatch++;
                var batch = toGeocode.Skip(i).Take(batchSize).ToList();
                Console.WriteLine($"[BATCH] Processing batch {currentBatch}/{totalBatches} ({batch.Count} items)");

                var batchTasks = batch.Select(async row =>
                {
                    try
                    {
                        Console.WriteLine($"[BATCH] Geocoding {row.Name} at {row.Latitude},{row.Longitude}");
                        var address = await _wialonService.ResolveAddressAsync(row.Latitude!.Value, row.Longitude!.Value);
                        if (!string.IsNullOrWhiteSpace(address))
                        {
                            Console.WriteLine($"[BATCH] {row.Name} -> {address}");
                            row.Location = address;
                        }
                        else
                        {
                            Console.WriteLine($"[BATCH] {row.Name} -> NO ADDRESS (geocoding returned null)");
                        }
                        
                        // Update progress
                        GeocodingProgress++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BATCH] Error geocoding {row.Name}: {ex.Message}");
                    }
                }).ToList();

                await Task.WhenAll(batchTasks);
                
                // Small delay between batches to avoid rate limiting
                if (currentBatch < totalBatches)
                {
                    await Task.Delay(500);
                }
            }
            IsGeocoding = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BATCH] Background geocoding error: {ex.Message}");
            IsGeocoding = false;
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
    private void ExportToExcel()
    {
        if (Reports.Count == 0)
        {
            _appState.SetStatus("No reports to export. Load reports first.");
            return;
        }

        try
        {
            using (var workbook = new XLWorkbook())
            {
                // Separate reports into uncommunicative and updating
                var uncommunicative = Reports
                    .Where(r => r.CommunicationStatus.Contains("Uncommunicative"))
                    .ToList();
                var updating = Reports
                    .Where(r => r.CommunicationStatus == "Updating")
                    .ToList();

                // Create Uncommunicative worksheet
                CreateReportWorksheet(workbook, "Uncommunicative", uncommunicative);

                // Create Updating worksheet
                CreateReportWorksheet(workbook, "Updating", updating);

                // Save file
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string fileName = $"WialonReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                string filePath = Path.Combine(desktopPath, fileName);

                workbook.SaveAs(filePath);
                _appState.SetStatus($"Report exported successfully to {fileName}");
            }
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Error exporting report: {ex.Message}");
        }
    }

    private void CreateReportWorksheet(XLWorkbook workbook, string sheetName, List<WialonReportRow> reports)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);

        // Add headers
        worksheet.Cell(1, 1).Value = "Vehicle Name";
        worksheet.Cell(1, 2).Value = "Client/Account";
        worksheet.Cell(1, 3).Value = "Location";
        worksheet.Cell(1, 4).Value = "Last Update";
        worksheet.Cell(1, 5).Value = "Communication Status";
        worksheet.Cell(1, 6).Value = "Latitude";
        worksheet.Cell(1, 7).Value = "Longitude";

        // Style header row
        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.DarkGray;
        headerRow.Style.Font.FontColor = XLColor.White;

        // Add data rows
        int row = 2;
        foreach (var report in reports)
        {
            worksheet.Cell(row, 1).Value = report.Name;
            worksheet.Cell(row, 2).Value = report.Client;
            worksheet.Cell(row, 3).Value = report.Location;
            worksheet.Cell(row, 4).Value = report.LastUpdateAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
            worksheet.Cell(row, 5).Value = report.CommunicationStatus;
            worksheet.Cell(row, 6).Value = report.Latitude ?? 0;
            worksheet.Cell(row, 7).Value = report.Longitude ?? 0;
            row++;
        }

        // Add summary row
        worksheet.Cell(row + 1, 1).Value = $"Total: {reports.Count}";
        worksheet.Cell(row + 1, 1).Style.Font.Bold = true;

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();
    }

    [RelayCommand]
    private void Disconnect()
    {
        _wialonService?.Dispose();
        _wialonService = null;
        IsConnected = false;
        WialonToken = "";
        Reports.Clear();
        _allReports.Clear();
        _cachedReports.Clear();  // Clear cache on disconnect
        _clientIds.Clear();
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
        
        // Sort by communication status (most uncommunicative first) then by name alphabetically
        var sorted = filtered
            .OrderBy(r => r.CommunicationStatus switch
            {
                "Uncommunicative (>14d)" => 0,  // Most critical - first
                "Uncommunicative (<14d)" => 1,  // Moderately critical
                "Updating" => 2,                 // Healthy
                _ => 3                           // Unknown
            })
            .ThenBy(r => r.Name)  // Then alphabetically by name
            .ToList();
        
        foreach (var report in sorted)
        {
            Reports.Add(report);
        }
    }
}
