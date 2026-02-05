using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StingListManager.Services;

public class WialonReport
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Location { get; set; } = "";
    public DateTime? LastUpdateAt { get; set; }
    public string Status { get; set; } = "";
    public string Url { get; set; } = "";
    public int AccountId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class WialonApiService
{
    private readonly string _baseUrl = "https://hst-api.wialon.eu/";  // Changed from .com to .eu
    private readonly string _token;
    private readonly HttpClient _httpClient;
    private string? _sessionId;
    public string? LastError { get; private set; }

    public WialonApiService(string token)
    {
        _token = token;
        _httpClient = new HttpClient();
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            LastError = null;
            
            // Build the login request according to Wialon API spec
            var loginParams = new
            {
                token = _token
            };

            var paramsJson = JsonSerializer.Serialize(loginParams);
            
            // Correct endpoint with /wialon/ajax.html path
            var url = $"{_baseUrl}wialon/ajax.html?svc=token/login&params={Uri.EscapeDataString(paramsJson)}";
            
            System.Diagnostics.Debug.WriteLine($"Connecting to Wialon: {url}");
            
            // Send GET request with query parameters
            var response = await _httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"Wialon Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"Wialon Response Content (first 500 chars): {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
            
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"HTTP {response.StatusCode}";
                return false;
            }

            // Check if response is empty
            if (string.IsNullOrWhiteSpace(responseContent))
            {
                LastError = "Empty response from Wialon API";
                return false;
            }

            // Try to parse JSON response
            JsonDocument jsonDoc;
            try
            {
                jsonDoc = JsonDocument.Parse(responseContent);
            }
            catch (JsonException ex)
            {
                LastError = $"Invalid JSON response: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {LastError}");
                return false;
            }
            
            var root = jsonDoc.RootElement;
            
            // Check for error in response
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMsg = errorElement.GetString() ?? "Unknown error";
                LastError = $"Wialon API Error: {errorMsg}";
                System.Diagnostics.Debug.WriteLine($"Wialon Error: {errorMsg}");
                return false;
            }
            
            // Check if we got a valid session ID in the response
            if (root.TryGetProperty("eid", out var eidElement))
            {
                _sessionId = eidElement.GetString();
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    System.Diagnostics.Debug.WriteLine($"Wialon Login Successful. Session ID: {_sessionId}");
                    return true;
                }
            }

            LastError = "No session ID in response";
            return false;
        }
        catch (Exception ex)
        {
            LastError = $"Connection Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Wialon connection exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<(List<WialonReport> reports, int totalCount)> GetReportsAsync(int from = 0, int batchSize = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                throw new Exception("Not connected to Wialon. Please connect first.");
            }

            var reports = new List<WialonReport>();

            // First, search for all resources/accounts to get a mapping of account ID to name
            var accountsMap = new Dictionary<int, string>();
            try
            {
                var accountSearchParams = new
                {
                    spec = new
                    {
                        itemsType = "avl_resource",  // Resources are the accounts that own units
                        propName = "sys_name",
                        propValueMask = "*",
                        sortType = "sys_name"
                    },
                    force = 1,
                    flags = 1,
                    from = 0,
                    to = 1000
                };

                var accountParamsJson = JsonSerializer.Serialize(accountSearchParams);
                var accountUrl = $"{_baseUrl}wialon/ajax.html?svc=core/search_items&params={Uri.EscapeDataString(accountParamsJson)}&sid={_sessionId}";
                
                System.Diagnostics.Debug.WriteLine($"Searching for resources/accounts: {accountUrl}");
                
                var accountResponse = await _httpClient.GetAsync(accountUrl);
                var accountContent = await accountResponse.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"Account search response: {accountContent}");
                
                if (accountResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_accounts_response.json"), accountContent);
                    }
                    catch
                    {
                        // Ignore logging failures
                    }

                    var accountDoc = JsonDocument.Parse(accountContent);
                    if (accountDoc.RootElement.TryGetProperty("items", out var accountItems) && accountItems.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var account in accountItems.EnumerateArray())
                        {
                            var accountId = account.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0;
                            var accountName = account.TryGetProperty("nm", out var nmElem) ? nmElem.GetString() : "";
                            if (accountId > 0 && !string.IsNullOrEmpty(accountName))
                            {
                                accountsMap[accountId] = accountName ?? "Unknown Account";
                                System.Diagnostics.Debug.WriteLine($"Found resource: ID={accountId}, Name={accountName}");
                            }
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded {accountsMap.Count} resources/accounts");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load accounts: {ex.Message}");
            }

            // Search for all units (vehicles) in Wialon with pagination
            var searchParams = new
            {
                spec = new
                {
                    itemsType = "avl_unit",  // Changed from avl_resource to avl_unit for vehicles
                    propName = "sys_name",
                    propValueMask = "*",
                    sortType = "sys_name"
                },
                force = 1,
                flags = 9217,  // 1 (basic) + 1024 (last position) + 8192 (custom fields/properties)
                from = from,
                to = from + batchSize   // Load in batches
            };

            var paramsJson = JsonSerializer.Serialize(searchParams);
            var url = $"{_baseUrl}wialon/ajax.html?svc=core/search_items&params={Uri.EscapeDataString(paramsJson)}&sid={_sessionId}";
            
            System.Diagnostics.Debug.WriteLine($"Searching for units/vehicles from {from} to {from + batchSize}: {url}");
            
            var response = await _httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API Error: HTTP {response.StatusCode}");
            }

            var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;
            
            // Check for API error
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMsg = errorElement.GetString() ?? "Unknown error";
                throw new Exception($"Wialon API Error: {errorMsg}");
            }
            
            int totalCount = 0;
            // Get total count if available
            if (root.TryGetProperty("totalItemsCount", out var countElement))
            {
                totalCount = countElement.GetInt32();
            }
            
            // Get items from response
            var addressCache = new Dictionary<string, string>();

            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    if (reports.Count == 0)
                    {
                        var availableFields = string.Join(", ", item.EnumerateObject().Select(p => p.Name));
                        System.Diagnostics.Debug.WriteLine($"Unit fields: {availableFields}");

                        try
                        {
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_first_unit.json"), item.GetRawText());
                        }
                        catch
                        {
                            // Ignore logging failures
                        }
                    }

                    var name = item.TryGetProperty("nm", out var nmElement) ? nmElement.GetString() : "Unknown";
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : 0;
                    
                    // Try to get unique ID (like VIN or registration)
                    var uniqueId = item.TryGetProperty("uid", out var uidElement) ? uidElement.GetString() : "";
                    
                    // Get billing account ID and map to account name
                    var accountId = item.TryGetProperty("bact", out var bactElement) ? bactElement.GetInt32() : 0;
                    System.Diagnostics.Debug.WriteLine($"Unit '{name}' has bact={accountId}");
                    
                    var client = accountId > 0 && accountsMap.ContainsKey(accountId) 
                        ? accountsMap[accountId] 
                        : "Unknown Account";
                    
                    System.Diagnostics.Debug.WriteLine($"Unit '{name}' mapped to client: {client}");
                    
                    // Try resource ID if billing account is missing
                    if (accountId == 0)
                    {
                        var resourceId = 0;
                        if (item.TryGetProperty("rid", out var ridElement) && ridElement.TryGetInt32(out var ridValue))
                            resourceId = ridValue;
                        else if (item.TryGetProperty("r", out var rElement) && rElement.TryGetInt32(out var rValue))
                            resourceId = rValue;
                        else if (item.TryGetProperty("res", out var resElement) && resElement.TryGetInt32(out var resValue))
                            resourceId = resValue;
                        else if (item.TryGetProperty("res_id", out var resIdElement) && resIdElement.TryGetInt32(out var resIdValue))
                            resourceId = resIdValue;

                        if (resourceId > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Unit '{name}' has resourceId={resourceId}");
                            if (accountsMap.TryGetValue(resourceId, out var resourceName))
                            {
                                accountId = resourceId;
                                client = resourceName;
                                System.Diagnostics.Debug.WriteLine($"Unit '{name}' mapped to resource: {client}");
                            }
                        }
                    }

                    // Get creator ID as fallback
                    if (accountId == 0 && item.TryGetProperty("crt", out var crtElement))
                    {
                        var creatorId = crtElement.GetInt32();
                        System.Diagnostics.Debug.WriteLine($"Unit '{name}' has crt={creatorId}");
                        if (creatorId > 0 && accountsMap.ContainsKey(creatorId))
                        {
                            accountId = creatorId;
                            client = accountsMap[creatorId];
                            System.Diagnostics.Debug.WriteLine($"Unit '{name}' using creator as client: {client}");
                        }
                    }
                    
                    // Get unit location (address preferred, fallback to geocode or lat/lon)
                    var location = "";
                    double? latValue = null;
                    double? lonValue = null;
                    if (item.TryGetProperty("pos", out var posElement) && posElement.ValueKind == JsonValueKind.Object)
                    {
                        if (posElement.TryGetProperty("y", out var latElement) &&
                            posElement.TryGetProperty("x", out var lonElement) &&
                            latElement.TryGetDouble(out var lat) &&
                            lonElement.TryGetDouble(out var lon))
                        {
                            latValue = lat;
                            lonValue = lon;
                        }
                    }

                    // Set initial location to placeholder - geocoding will update this
                    if (latValue.HasValue && lonValue.HasValue)
                    {
                        location = "Loading address...";
                    }
                    else
                    {
                        location = "Unknown";
                    }

                    // Get last update time (prefer last message, fallback to position time)
                    DateTime? lastUpdateAt = null;
                    if (item.TryGetProperty("lmsg", out var lmsgElement))
                    {
                        if (lmsgElement.ValueKind == JsonValueKind.Number && lmsgElement.TryGetInt64(out var lmsgTime) && lmsgTime > 0)
                        {
                            lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(lmsgTime).DateTime;
                        }
                        else if (lmsgElement.ValueKind == JsonValueKind.Object &&
                                 lmsgElement.TryGetProperty("t", out var lmsgTimeElement) &&
                                 lmsgTimeElement.TryGetInt64(out var lmsgObjectTime) && lmsgObjectTime > 0)
                        {
                            lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(lmsgObjectTime).DateTime;
                        }
                    }

                    if (lastUpdateAt is null && item.TryGetProperty("pos", out var posElementForTime) &&
                        posElementForTime.ValueKind == JsonValueKind.Object &&
                        posElementForTime.TryGetProperty("t", out var posTimeElement) &&
                        posTimeElement.TryGetInt64(out var posTime) && posTime > 0)
                    {
                        lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(posTime).DateTime;
                    }

                    // Get creation time (Unix timestamp)
                    var createdAt = DateTime.Now;
                    if (item.TryGetProperty("ct", out var ctElement))
                    {
                        var unixTime = ctElement.GetInt64();
                        createdAt = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                    }
                    
                    reports.Add(new WialonReport
                    {
                        Id = id,
                        Name = name ?? "Unknown Vehicle",
                        Client = client,
                        CreatedAt = createdAt,
                        Location = location,
                        LastUpdateAt = lastUpdateAt,
                        Status = "Active",
                        Url = $"unit_{id}",
                        AccountId = accountId,
                        Latitude = latValue,
                        Longitude = lonValue
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"Found {reports.Count} vehicles in this batch");
            return (reports, totalCount);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to fetch vehicles from Wialon: {ex.Message}");
        }
    }

    public async Task<string?> ResolveAddressAsync(double lat, double lon)
    {
        try
        {
            // Use Wialon Pro's gis/get_locations endpoint
            var pointsArray = new[]
            {
                new
                {
                    x = lon,
                    y = lat
                }
            };

            var paramsJson = JsonSerializer.Serialize(new { points = pointsArray });
            var url = $"{_baseUrl}wialon/ajax.html?svc=gis/get_locations&params={Uri.EscapeDataString(paramsJson)}&sid={_sessionId}";

            System.Diagnostics.Debug.WriteLine($"Geocoding: {lat},{lon}");
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_geocode_response.json"), content);
            }
            catch
            {
                // Ignore logging failures
            }

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
            {
                System.Diagnostics.Debug.WriteLine($"Geocode HTTP error: {response.StatusCode}");
                return null;
            }

            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            // Wialon Pro response format: {"locations": ["Address string"]}
            if (root.TryGetProperty("locations", out var locationsElement) && 
                locationsElement.ValueKind == JsonValueKind.Array && 
                locationsElement.GetArrayLength() > 0)
            {
                var first = locationsElement[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    var address = first.GetString();
                    System.Diagnostics.Debug.WriteLine($"Geocoded address: {address}");
                    return address;
                }
            }

            System.Diagnostics.Debug.WriteLine($"Geocode returned empty or invalid response: {content}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Geocode failed for {lat},{lon}: {ex.Message}");
            return null;
        }
    }

    public async Task<string> GenerateReportAsync(string reportType, DateTime startDate, DateTime endDate)
    {
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                throw new Exception("Not connected to Wialon. Please connect first.");
            }

            // Placeholder for report generation
            // Real implementation would call Wialon API with specific report parameters
            return $"Report generated for {reportType} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate report: {ex.Message}");
        }
    }

    public async Task<bool> LogoutAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
                return true;

            var logoutParams = new { };
            var paramsJson = JsonSerializer.Serialize(logoutParams);
            var url = $"{_baseUrl}wialon/ajax.html?svc=core/logout&sid={_sessionId}&params={Uri.EscapeDataString(paramsJson)}";
            
            await _httpClient.GetAsync(url);
            _sessionId = null;
            return true;
        }
        catch
        {
            _sessionId = null;
            return false;
        }
    }

    public void Dispose()
    {
        LogoutAsync().Wait();
        _httpClient?.Dispose();
    }
}
