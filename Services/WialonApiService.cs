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
    public string? Make { get; set; }
    public string? Model { get; set; }
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
    private static readonly object GeocodeLogLock = new();
    private static readonly string GeocodeLogPath = Path.Combine(AppContext.BaseDirectory, "wialon_geocode.log");

    public WialonApiService(string token)
    {
        _token = token;
        _httpClient = new HttpClient();
        // Set reasonable timeouts
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
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
                    if (_userId == 0)
                    {
                        await FetchUserIdAsync();
                    }

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
                
                if (accountResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(accountContent))
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_accounts_response.json"), accountContent);
                    }
                    catch
                    {
                        // Ignore logging failures
                    }

                    try
                    {
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
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Account search returned no items or invalid format");
                        }
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse account response: {parseEx.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Account search HTTP error: {accountResponse.StatusCode}");
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded {accountsMap.Count} resources/accounts");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load accounts: {ex.Message}");
            }

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
                flags = 9479,  // 1 (basic) + 2 (properties) + 4 (billing) + 256 (profile fields) + 1024 (last position) + 8192 (custom fields/properties)
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

                    var name = item.TryGetProperty("nm", out var nmElement) ? nmElement.GetString() : "Unknown";
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : 0;
                    
                    // Try to get unique ID (like VIN or registration)
                    var uniqueId = item.TryGetProperty("uid", out var uidElement) ? uidElement.GetString() : "";
                    
                    // Get billing account ID and map to account name
                    var accountId = item.TryGetProperty("bact", out var bactElement) ? bactElement.GetInt32() : 0;
                    System.Diagnostics.Debug.WriteLine($"Unit '{name}' has bact={accountId}");
                    
                    var client = accountId > 0 && accountsMap.ContainsKey(accountId) 
                        ? accountsMap[accountId] 
                        : "";
                    
                    System.Diagnostics.Debug.WriteLine($"Unit '{name}' mapped to client: {client}");
                    
                    // Try resource ID if billing account is missing
                    if (string.IsNullOrEmpty(client) && accountId == 0)
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
                                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_first_unit.json"), item.GetRawText());
                            }
                            catch
                            {
                                // Ignore logging failures
                            }
                        }

                    // Get creator ID as fallback
                    if (string.IsNullOrEmpty(client) && accountId == 0 && item.TryGetProperty("crt", out var crtElement))
                    {
                        var creatorId = crtElement.GetInt32();
                        System.Diagnostics.Debug.WriteLine($"Unit '{name}' has crt={creatorId}");
                        if (creatorId > 0 && accountsMap.ContainsKey(creatorId))
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
                    }
                    
                    // If still no client found, use a default placeholder
                    if (string.IsNullOrEmpty(client))
                    {
                        client = "Unknown Client";
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
                            if (posElement.TryGetProperty("a", out var addressElement) && addressElement.ValueKind == JsonValueKind.String)
                            {
                                var address = addressElement.GetString();
                                if (!string.IsNullOrWhiteSpace(address))
                                {
                                    location = address;
                                }
                            }

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
                        if (string.IsNullOrWhiteSpace(location))
                        {
                            if (latValue.HasValue && lonValue.HasValue)
                            {
                                location = FormattableString.Invariant($"{latValue:F4}, {lonValue:F4}");  // Show coordinates as fallback
                            }
                            else
                            {
                                location = "Unknown";
                            }
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
                    
                    var make = GetCustomFieldValue(item,
                        "make", "vehicle_make", "vehicle make", "car_make", "truck_make", "brand", "manufacturer");
                    var model = GetCustomFieldValue(item,
                        "model", "vehicle_model", "vehicle model", "car_model", "truck_model", "type");

                    if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
                    {
                        var profile = await GetMakeModelFromUnitProfileAsync(id);
                        make = string.IsNullOrWhiteSpace(make) ? profile.Make : make;
                        model = string.IsNullOrWhiteSpace(model) ? profile.Model : model;
                    }

                    reports.Add(new WialonReport
                    {
                        Id = id,
                        Name = name ?? "Unknown Vehicle",
                        Client = client,
                        Make = make,
                        Model = model,
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
        var cacheKey = FormattableString.Invariant($"{lat:F4},{lon:F4}");
        if (_geocodeCache.TryGetValue(cacheKey, out var cached))
        {
            Console.WriteLine($"[GEOCODE] Cache hit for {cacheKey}: {cached}");
            return cached;
        }

        if (_userId == 0 && !string.IsNullOrEmpty(_sessionId))
        {
            await FetchUserIdAsync();
        }

        if (_userId == 0)
        {
            AppendGeocodeLog("Wialon geocode skipped: userId=0");
        }

        var wialonAddress = await ResolveAddressFromWialonAsync(lat, lon);
        if (!string.IsNullOrWhiteSpace(wialonAddress))
        {
            _geocodeCache.TryAdd(cacheKey, wialonAddress);
            return wialonAddress;
        }

        try
        {
            // Use OpenStreetMap Nominatim for reverse geocoding (free, no API key required)
            var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={lat}&lon={lon}&zoom=18&addressdetails=1&accept-language=en";

            System.Diagnostics.Debug.WriteLine($"[Geocoding] Requesting: {url}");
            AppendGeocodeLog($"Request {lat},{lon} -> {url}");

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // Nominatim requires a User-Agent header (include project URL as contact)
                request.Headers.UserAgent.ParseAdd("StingListManager/1.0 (+https://github.com/keaganemanuel002-lab/Capital-air-stinglist-manager)");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en");
                request.Headers.Referrer = new Uri("https://github.com/keaganemanuel002-lab/Capital-air-stinglist-manager");

                try
                {
                    var response = await _httpClient.SendAsync(request, System.Threading.CancellationToken.None);
                    System.Diagnostics.Debug.WriteLine($"[Geocoding] Response status: {response.StatusCode}");
                    AppendGeocodeLog($"Response status: {(int)response.StatusCode} {response.StatusCode} (attempt {attempt}/3)");

                    if ((int)response.StatusCode == 429 || (int)response.StatusCode == 503)
                    {
                        var backoffMs = 1000 * attempt;
                        System.Diagnostics.Debug.WriteLine($"[Geocoding] Rate limited, retrying in {backoffMs}ms (attempt {attempt}/3)");
                        AppendGeocodeLog($"Rate limited. Retrying in {backoffMs}ms");
                        await Task.Delay(backoffMs);
                        continue;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[Geocoding] Response length: {content.Length} bytes");

                    try
                    {
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wialon_geocode_response.json"), content);
                    }
                    catch
                    {
                        // Ignore logging failures
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Geocoding] HTTP error: {response.StatusCode}");
                        AppendGeocodeLog($"HTTP error: {(int)response.StatusCode} {response.StatusCode}");
                        return null;
                    }

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Geocoding] Empty response");
                        AppendGeocodeLog("Empty response");
                        return null;
                    }

                    var jsonDoc = JsonDocument.Parse(content);
                    var root = jsonDoc.RootElement;

                    // OpenStreetMap Nominatim response format: {"address": {...}, "display_name": "..."}
                    if (root.TryGetProperty("display_name", out var displayNameElement) &&
                        displayNameElement.ValueKind == JsonValueKind.String)
                    {
                        var address = displayNameElement.GetString();
                        if (!string.IsNullOrWhiteSpace(address))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Geocoding] Success: {address.Substring(0, Math.Min(100, address.Length))}");
                            AppendGeocodeLog($"Success: {address}");
                            return address;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[Geocoding] No display_name in response");
                    AppendGeocodeLog("No display_name in response");
                    return null;
                }
                catch (TaskCanceledException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Geocoding] Timeout: {ex.Message}");
                    AppendGeocodeLog($"Timeout: {ex.Message}");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Geocoding] HTTP error: {ex.Message}");
                    AppendGeocodeLog($"HTTP error: {ex.Message}");
                    return null;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Geocoding] Exception for {lat},{lon}: {ex.GetType().Name}: {ex.Message}");
            AppendGeocodeLog($"Exception for {lat},{lon}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void AppendGeocodeLog(string message)
    {
        try
        {
            Paths.EnsureLocal();
            lock (GeocodeLogLock)
            {
                File.AppendAllText(GeocodeLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ignore logging failures
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
        _httpClient?.Dispose();
    }

    public async Task LogoutAndDisposeAsync()
    {
        try
        {
            await LogoutAsync();
        }
        catch
        {
            // Ignore logout failures during disposal
        }

        _httpClient?.Dispose();
    }
}
