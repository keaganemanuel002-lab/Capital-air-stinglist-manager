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
    private readonly string _baseUrl = "https://hst-api.wialon.eu/";
    private readonly string _token;
    private readonly HttpClient _httpClient;
    private string? _sessionId;
    private int _userId;  // User ID needed for geocoding API
    public string? LastError { get; private set; }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _geocodeCache = new();

    static WialonApiService()
    {
        // Maximize connection limit for parallel geocoding requests
        System.Net.ServicePointManager.DefaultConnectionLimit = 200;
        System.Net.ServicePointManager.Expect100Continue = false;
        System.Net.ServicePointManager.UseNagleAlgorithm = false;
    }

    public WialonApiService(string token)
    {
        _token = token;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);  // Longer timeout for Nominatim geocoding
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "StingListManager/1.0");
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
            
            // Correct endpoint path confirmed from browser network tab
            var url = $"{_baseUrl}wialon/ajax.html?svc=token/login&params={Uri.EscapeDataString(paramsJson)}";
            
            System.Diagnostics.Debug.WriteLine($"Connecting to Wialon: {url}");
            System.Diagnostics.Debug.WriteLine($"Base URL: {_baseUrl}");
            System.Diagnostics.Debug.WriteLine($"Token (first 10 chars): {_token.Substring(0, Math.Min(10, _token.Length))}...");
            
            // Try GET request first (some Wialon installations prefer this)
            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"Wialon Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Wialon Response Content: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
                
                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"HTTP {response.StatusCode}: {responseContent}";
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
                
                System.Diagnostics.Debug.WriteLine($"=== FULL LOGIN RESPONSE ===");
                System.Diagnostics.Debug.WriteLine(JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
                System.Diagnostics.Debug.WriteLine($"=== END LOGIN RESPONSE ===");
                
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
                    System.Diagnostics.Debug.WriteLine($"Wialon Session ID: {_sessionId}");
                }

                // Try to get user ID from response - it might be 'uid' or part of user object
                if (root.TryGetProperty("uid", out var uidElement))
                {
                    if (uidElement.TryGetInt32(out var uid))
                    {
                        _userId = uid;
                        System.Diagnostics.Debug.WriteLine($"Wialon User ID (from uid): {_userId}");
                    }
                }
                else if (root.TryGetProperty("user", out var userElement) && userElement.ValueKind == JsonValueKind.Object)
                {
                    // User might be nested in an object
                    if (userElement.TryGetProperty("id", out var userIdElement) && userIdElement.TryGetInt32(out var userId))
                    {
                        _userId = userId;
                        System.Diagnostics.Debug.WriteLine($"Wialon User ID (from user.id): {_userId}");
                    }
                }
                else if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
                {
                    _userId = id;
                    System.Diagnostics.Debug.WriteLine($"Wialon User ID (from id): {_userId}");
                }
                
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    System.Diagnostics.Debug.WriteLine($"Wialon Login Successful. Session ID: {_sessionId}, User ID: {_userId}");
                    return true;
                }

                LastError = "Missing session ID in response";
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastError = $"Network Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"HTTP Request Exception: {ex}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                LastError = $"Request Timeout: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Timeout Exception: {ex}");
                return false;
            }
        }
        catch (Exception ex)
        {
            LastError = $"Connection Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Wialon connection exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<Dictionary<int, string>> GetClientsAsync()
    {
        var accountsMap = await FetchItemsMapAsync("avl_resource", "resource");
        var userMap = await FetchItemsMapAsync("user", "user");

        foreach (var pair in userMap)
        {
            accountsMap[pair.Key] = pair.Value;
        }

        System.Diagnostics.Debug.WriteLine($"Loaded {accountsMap.Count} resources/users");
        return accountsMap;
    }

    public async Task<Dictionary<int, string>> GetResourcesAsync()
    {
        var resources = await FetchItemsMapAsync("avl_resource", "resource");
        System.Diagnostics.Debug.WriteLine($"Loaded {resources.Count} resources");
        return resources;
    }

    private async Task<Dictionary<int, string>> FetchItemsMapAsync(string itemsType, string label)
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            throw new Exception("Not connected to Wialon. Please connect first.");
        }

        var itemsMap = new Dictionary<int, string>();
        try
        {
            var searchParams = new
            {
                spec = new
                {
                    itemsType = itemsType,
                    propName = "sys_name",
                    propValueMask = "*",
                    sortType = "sys_name"
                },
                force = 1,
                flags = 1,
                from = 0,
                to = 1000
            };

            var paramsJson = JsonSerializer.Serialize(searchParams);
            var url = $"{_baseUrl}wialon/ajax.html?svc=core/search_items&params={Uri.EscapeDataString(paramsJson)}&sid={_sessionId}";

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var itemId = item.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0;
                        var itemName = item.TryGetProperty("nm", out var nmElem) ? nmElem.GetString() : "";
                        if (itemId > 0 && !string.IsNullOrEmpty(itemName))
                        {
                            itemsMap[itemId] = itemName ?? "";
                            System.Diagnostics.Debug.WriteLine($"Found {label}: ID={itemId}, Name={itemName}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load {label}s: {ex.Message}");
        }

        return itemsMap;
    }

    public async Task<(List<WialonReport> reports, int totalCount)> GetReportsAsync(int from = 0, int batchSize = 100, string? propName = null, string? propValueMask = null)
    {
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                throw new Exception("Not connected to Wialon. Please connect first.");
            }

            var reports = new List<WialonReport>();

            var accountsMap = await GetClientsAsync();

            // Search for all units (vehicles) in Wialon with pagination
            var effectivePropName = string.IsNullOrWhiteSpace(propName) ? "sys_name" : propName;
            var effectiveValueMask = string.IsNullOrWhiteSpace(propValueMask) ? "*" : propValueMask;

            var searchParams = new
            {
                spec = new
                {
                    itemsType = "avl_unit",  // Changed from avl_resource to avl_unit for vehicles
                    propName = effectivePropName,
                    propValueMask = effectiveValueMask,
                    sortType = "sys_name"
                },
                force = 1,
                flags = 9221,  // 1 (basic) + 4 (billing) + 1024 (last position) + 8192 (custom fields/properties)
                from = from,
                to = from + batchSize   // Load in batches
            };

            var paramsJson = JsonSerializer.Serialize(searchParams);
            var url = $"{_baseUrl}wialon/ajax.html?svc=core/search_items&params={Uri.EscapeDataString(paramsJson)}&sid={_sessionId}";
            
            System.Diagnostics.Debug.WriteLine($"Searching for units/vehicles from {from} to {from + batchSize}: {url}");
            
            int totalCount = 0;
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
                        System.Diagnostics.Debug.WriteLine($"\n=== Processing Unit: {name} (ID: {id}) ===");
                        System.Diagnostics.Debug.WriteLine($"  bact={accountId}, uid={uniqueId}");
                        System.Diagnostics.Debug.WriteLine($"  accountsMap has {accountsMap.Count} entries");
                        
                        var client = "";
                        if (accountId > 0)
                        {
                            if (accountsMap.ContainsKey(accountId))
                            {
                                client = accountsMap[accountId];
                                System.Diagnostics.Debug.WriteLine($"  Found in accountsMap: '{client}'");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"  bact={accountId} NOT found in accountsMap");
                            }
                        }
                        
                        // Try resource ID if billing account is missing
                        if (string.IsNullOrEmpty(client))
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

                            System.Diagnostics.Debug.WriteLine($"  Trying resourceId={resourceId}");

                            if (resourceId > 0)
                            {
                                if (accountsMap.TryGetValue(resourceId, out var resourceName))
                                {
                                    accountId = resourceId;
                                    client = resourceName;
                                    System.Diagnostics.Debug.WriteLine($"  Found via resourceId: '{client}'");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"  resourceId={resourceId} NOT found in accountsMap");
                                }
                            }
                        }

                        // Get creator ID as fallback
                        if (string.IsNullOrEmpty(client) && item.TryGetProperty("crt", out var crtElement))
                        {
                            var creatorId = crtElement.GetInt32();
                            System.Diagnostics.Debug.WriteLine($"  Trying creatorId={creatorId}");
                            if (creatorId > 0 && accountsMap.ContainsKey(creatorId))
                            {
                                accountId = creatorId;
                                client = accountsMap[creatorId];
                                System.Diagnostics.Debug.WriteLine($"  Found via creator: '{client}'");
                            }
                            else if (creatorId > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"  creatorId={creatorId} NOT found in accountsMap");
                            }
                        }

                        // Try account group as another fallback
                        if (string.IsNullOrEmpty(client) && item.TryGetProperty("ag", out var agElement))
                        {
                            var accountGroupId = agElement.GetInt32();
                            System.Diagnostics.Debug.WriteLine($"  Trying accountGroupId={accountGroupId}");
                            if (accountGroupId > 0 && accountsMap.ContainsKey(accountGroupId))
                            {
                                accountId = accountGroupId;
                                client = accountsMap[accountGroupId];
                                System.Diagnostics.Debug.WriteLine($"  Found via account group: '{client}'");
                            }
                            else if (accountGroupId > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"  accountGroupId={accountGroupId} NOT found in accountsMap");
                            }
                        }

                        // If still no client found, just use the vehicle name itself
                        if (string.IsNullOrEmpty(client))
                        {
                            client = name ?? "Unknown";
                            System.Diagnostics.Debug.WriteLine($"  Using vehicle name as client: '{client}'");
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

                    // Set initial location to coordinates - geocoding will update this if successful
                    if (latValue.HasValue && lonValue.HasValue)
                    {
                        location = $"{latValue:F4}, {lonValue:F4}";  // Show coordinates as fallback
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
        // Check cache first
        var cacheKey = $"{lat:F4},{lon:F4}";
        if (_geocodeCache.TryGetValue(cacheKey, out var cached))
        {
            Console.WriteLine($"[GEOCODE] Cache hit for {cacheKey}: {cached}");
            return cached;
        }

        try
        {
            Console.WriteLine($"[GEOCODE] Geocoding {lat},{lon}");

            // Use OpenStreetMap Nominatim API (free, no API key needed)
            var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={lat}&lon={lon}";

            Console.WriteLine($"[GEOCODE] URL: {url}");

            var response = await _httpClient.GetAsync(url);
            Console.WriteLine($"[GEOCODE] Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GEOCODE] Failed with status {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GEOCODE] Response: {content.Substring(0, Math.Min(200, content.Length))}");

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"[GEOCODE] Empty response");
                return null;
            }

            try
            {
                var jsonDoc = JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                // Try to get the address from the response
                if (root.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object)
                {
                    var address = BuildAddressString(addressElement);
                    if (!string.IsNullOrEmpty(address))
                    {
                        Console.WriteLine($"[GEOCODE] SUCCESS: {address}");
                        _geocodeCache.TryAdd(cacheKey, address);
                        return address;
                    }
                }

                // Fallback: try display_name
                if (root.TryGetProperty("display_name", out var displayElement))
                {
                    var displayName = displayElement.GetString();
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        Console.WriteLine($"[GEOCODE] SUCCESS (display_name): {displayName}");
                        _geocodeCache.TryAdd(cacheKey, displayName);
                        return displayName;
                    }
                }

                Console.WriteLine($"[GEOCODE] No address found in response");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GEOCODE] JSON parse error: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GEOCODE] Exception: {ex.Message}");
            return null;
        }
    }

    private string BuildAddressString(JsonElement addressElement)
    {
        var parts = new List<string>();

        // Build address from components
        var fields = new[] { "house_number", "road", "suburb", "city", "county", "state", "postcode", "country" };
        
        foreach (var field in fields)
        {
            if (addressElement.TryGetProperty(field, out var element))
            {
                var value = element.GetString();
                if (!string.IsNullOrEmpty(value) && !parts.Contains(value))
                {
                    parts.Add(value);
                }
            }
        }

        return string.Join(", ", parts);
    }

    private async Task FetchUserIdAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                Console.WriteLine($"[FETCHUID] No session ID");
                return;
            }

            // Call account/get_account_info to get user ID
            var paramsJson = JsonSerializer.Serialize(new { });
            var url = $"{_baseUrl}wialon/ajax.html?svc=account/get_account_info&sid={_sessionId}&params={Uri.EscapeDataString(paramsJson)}";

            Console.WriteLine($"[FETCHUID] Calling API...");

            var response = await _httpClient.GetAsync(url);
            Console.WriteLine($"[FETCHUID] Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FETCHUID] Failed");
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[FETCHUID] Content: {content.Substring(0, Math.Min(200, content.Length))}");

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"[FETCHUID] Empty response");
                return;
            }

            var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            // Try to extract user ID
            if (root.TryGetProperty("id", out var idElement))
            {
                if (idElement.TryGetInt32(out var id))
                {
                    _userId = id;
                    Console.WriteLine($"[FETCHUID] Success - got {_userId}");
                    return;
                }
            }

            if (root.TryGetProperty("uid", out var uidElement))
            {
                if (uidElement.TryGetInt32(out var uid))
                {
                    _userId = uid;
                    Console.WriteLine($"[FETCHUID] Success (uid field) - got {_userId}");
                    return;
                }
            }

            Console.WriteLine($"[FETCHUID] Could not extract ID");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FETCHUID] Error: {ex.Message}");
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
            _userId = 0;
            return true;
        }
        catch
        {
            _sessionId = null;
            _userId = 0;
            return false;
        }
    }

    public void Dispose()
    {
        LogoutAsync().Wait();
        _httpClient?.Dispose();
    }
}
