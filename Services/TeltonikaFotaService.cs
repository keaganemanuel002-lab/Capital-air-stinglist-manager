using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StingListManager.Services;

public class TeltonikaDeviceInfo
{
    [JsonPropertyName("imei")]
    public string? Imei { get; set; }
    
    [JsonPropertyName("serial")]
    public string? SerialNumber { get; set; }
    
    [JsonPropertyName("iccid")]
    public string? Iccid { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class TeltonikaDeviceResponse
{
    [JsonPropertyName("data")]
    public TeltonikaDeviceInfo? Data { get; set; }
    
    [JsonPropertyName("status")]
    public int Status { get; set; } = 200;
}

public class TeltonikaFotaService
{
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://fota.teltonika-networks.com/api";

    public TeltonikaFotaService(string? apiKey = null)
    {
        _apiKey = apiKey ?? new SettingsService().Load().TeltonikaApiKey;
        _httpClient = new HttpClient();
        
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<TeltonikaDeviceInfo?> GetDeviceInfoAsync(string imeiOrSerial)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Teltonika API key not configured");

        try
        {
            // Try different API endpoints that Teltonika FOTA might use
            var endpoints = new[]
            {
                $"{BaseUrl}/devices/{imeiOrSerial}",
                $"{BaseUrl}/v1/devices/{imeiOrSerial}",
                $"{BaseUrl}/devices?query={imeiOrSerial}"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        
                        // Try parsing as wrapped response first
                        var wrapped = JsonSerializer.Deserialize<TeltonikaDeviceResponse>(json);
                        if (wrapped?.Data != null)
                            return wrapped.Data;
                        
                        // Try parsing as direct device object
                        var direct = JsonSerializer.Deserialize<TeltonikaDeviceInfo>(json);
                        if (direct != null)
                            return direct;
                    }
                }
                catch (Exception)
                {
                    // Try next endpoint
                    continue;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching Teltonika device info for '{imeiOrSerial}': {ex.Message}");
            return null;
        }
    }

    public async Task<List<TeltonikaDeviceInfo>> GetAllDevicesAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Teltonika API key not configured");

        try
        {
            var endpoints = new[] 
            { 
                $"{BaseUrl}/devices",
                $"{BaseUrl}/v1/devices"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        
                        // Try parsing as array
                        var devices = JsonSerializer.Deserialize<List<TeltonikaDeviceInfo>>(json);
                        if (devices != null && devices.Count > 0)
                            return devices;
                        
                        // Try parsing as wrapped response
                        var wrapped = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                        if (wrapped != null && wrapped.ContainsKey("data"))
                        {
                            var data = wrapped["data"];
                            var parsed = JsonSerializer.Deserialize<List<TeltonikaDeviceInfo>>(data.GetRawText());
                            if (parsed != null)
                                return parsed;
                        }
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            return new List<TeltonikaDeviceInfo>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching Teltonika devices: {ex.Message}");
            return new List<TeltonikaDeviceInfo>();
        }
    }

    public async Task<TeltonikaDeviceInfo?> SearchByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var devices = await GetAllDevicesAsync();
        
        // Try to match by name or model that contains the code
        return devices.FirstOrDefault(d => 
            (d.Name != null && d.Name.Contains(code, StringComparison.OrdinalIgnoreCase)) ||
            (d.Model != null && d.Model.Contains(code, StringComparison.OrdinalIgnoreCase)));
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_apiKey);
    }
}
