using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StingListManager.Services;

public class FlickswitchSimInfo
{
    public string? Iccid { get; set; }
    public string? SimNumber { get; set; }
    public string? Imsi { get; set; }
    public string? Status { get; set; }
    public string? NetworkStatus { get; set; }
    public decimal? AirtimeBalance { get; set; }
    public decimal? DataBalanceMb { get; set; }
    public decimal? SmsBalance { get; set; }
    public DateTimeOffset? LastBalanceCheckAt { get; set; }
    public List<string> Rules { get; set; } = new();
}

public class FlickswitchSimControlService
{
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;

    public string? LastError { get; private set; }

    private enum AuthMode
    {
        ApiKeyHeader,
        ApiKeyLowerHeader,
        AuthorizationApiKey,
        Bearer,
        RawAuthorization
    }

    public FlickswitchSimControlService(AppSettings settings)
        : this(settings.FlickswitchApiKey, settings.FlickswitchBaseUrl)
    {
    }

    public FlickswitchSimControlService(string? apiKey = null, string? baseUrl = null)
    {
        var loaded = new SettingsService().Load();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? loaded.FlickswitchApiKey : apiKey;
        _baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(baseUrl) ? loaded.FlickswitchBaseUrl : baseUrl);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return false;

        return !_apiKey.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<FlickswitchSimInfo?> FindByIccidOrPhoneAsync(string? iccid, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (!IsConfigured())
        {
            LastError = "Flickswitch API key is missing or invalid.";
            return null;
        }

        var normalizedIccid = NormalizeDigits(iccid);
        var normalizedPhone = NormalizeDigits(phoneNumber);

        var iccidTerms = BuildSearchTerms(iccid, includeDigits: true);
        foreach (var term in iccidTerms)
        {
            var sims = await QuerySimsByFilterAsync("iccid", term, cancellationToken);
            var bestMatch = SelectBestMatch(sims, normalizedIccid, normalizedPhone);
            if (bestMatch != null)
                return bestMatch;
        }

        var phoneTerms = BuildSearchTerms(phoneNumber, includeDigits: true);
        foreach (var term in phoneTerms)
        {
            var sims = await QuerySimsByFilterAsync("msisdn", term, cancellationToken);
            var bestMatch = SelectBestMatch(sims, normalizedIccid, normalizedPhone);
            if (bestMatch != null)
                return bestMatch;
        }

        return null;
    }

    public async Task<(bool ok, string message)> UpdateSimDescriptionAsync(
        string? iccid,
        string? phoneNumber,
        string? imsi,
        string? description,
        CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (!IsConfigured())
        {
            LastError = "Flickswitch API key is missing or invalid.";
            return (false, LastError);
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : description.Trim();

        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            LastError = "SIM description is required.";
            return (false, LastError);
        }

        if (!TryBuildUpdateIdentifier(iccid, phoneNumber, imsi, out var identifierName, out var identifierValue, out var validationError))
        {
            LastError = validationError;
            return (false, LastError);
        }

        var endpoint = $"{_baseUrl}/api/sims?{identifierName}={Uri.EscapeDataString(identifierValue)}";
        return await TryUpdateSimDescriptionAsync(endpoint, normalizedDescription, cancellationToken);
    }

    public async Task<(bool ok, string message)> RequestSimBalancesRefreshAsync(
        string? iccid,
        string? phoneNumber,
        string? imsi,
        CancellationToken cancellationToken = default)
    {
        LastError = null;

        if (!IsConfigured())
        {
            LastError = "Flickswitch API key is missing or invalid.";
            return (false, LastError);
        }

        if (!TryBuildUpdateIdentifier(iccid, phoneNumber, imsi, out var identifierName, out var identifierValue, out var validationError))
        {
            LastError = validationError;
            return (false, LastError);
        }

        var encodedId = Uri.EscapeDataString(identifierValue);
        var endpoints = new[]
        {
            $"{_baseUrl}/api/sims-balances?{identifierName}={encodedId}",
            $"{_baseUrl}/api/sim-balances?{identifierName}={encodedId}",
            $"{_baseUrl}/api/sims/balances?{identifierName}={encodedId}",
            $"{_baseUrl}/api/sims/balance?{identifierName}={encodedId}",
            $"{_baseUrl}/api/sim_balances?{identifierName}={encodedId}"
        };

        foreach (var endpoint in endpoints)
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
            {
                var result = await TryRequestSimBalancesAsync(endpoint, method, cancellationToken);
                if (result.ok)
                    return result;

                // If endpoint or method is unsupported, try next candidate.
                if (!string.IsNullOrWhiteSpace(LastError)
                    && (LastError.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
                        || LastError.Contains("HTTP 405", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                return result;
            }
        }

        return (false, LastError ?? "SIM balance request endpoint was not found.");
    }

    private async Task<List<FlickswitchSimInfo>> QuerySimsByFilterAsync(string filterName, string filterValue, CancellationToken cancellationToken)
    {
        var encodedValue = Uri.EscapeDataString(filterValue);
        var endpoint = $"{_baseUrl}/api/sims?status=ALL&page=1&page_size=50&{filterName}={encodedValue}";
        return await TryFetchSimsAsync(endpoint, cancellationToken);
    }

    private async Task<List<FlickswitchSimInfo>> TryFetchSimsAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            LastError = "Flickswitch API key is missing.";
            return new List<FlickswitchSimInfo>();
        }

        string? authError = null;

        foreach (var mode in new[] { AuthMode.ApiKeyHeader, AuthMode.ApiKeyLowerHeader, AuthMode.AuthorizationApiKey, AuthMode.Bearer, AuthMode.RawAuthorization })
        {
            using var request = BuildRequest(HttpMethod.Get, endpoint, mode);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    authError = BuildUnauthorizedError(response.StatusCode, mode, body);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LastError = FormatHttpError(response.StatusCode, body);
                    return new List<FlickswitchSimInfo>();
                }

                var sims = ParseSims(body);
                if (sims.Count == 0)
                {
                    LastError = "Flickswitch returned no SIM data for this filter.";
                }

                return sims;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"Request failed while calling Flickswitch: {ex.Message}";
                return new List<FlickswitchSimInfo>();
            }
        }

        LastError = authError ?? "Flickswitch authorization failed.";
        return new List<FlickswitchSimInfo>();
    }

    private async Task<(bool ok, string message)> TryUpdateSimDescriptionAsync(string endpoint, string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            LastError = "Flickswitch API key is missing.";
            return (false, LastError);
        }

        string? authError = null;
        foreach (var mode in new[] { AuthMode.ApiKeyHeader, AuthMode.ApiKeyLowerHeader, AuthMode.AuthorizationApiKey, AuthMode.Bearer, AuthMode.RawAuthorization })
        {
            using var request = BuildRequest(HttpMethod.Patch, endpoint, mode, BuildUpdateDescriptionContent(description));

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    authError = BuildUnauthorizedError(response.StatusCode, mode, body);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.MethodNotAllowed)
                {
                    LastError = "HTTP 405: Flickswitch update requires PATCH /api/sims.";
                    return (false, LastError);
                }

                if (!response.IsSuccessStatusCode)
                {
                    LastError = FormatHttpError(response.StatusCode, body);
                    return (false, LastError);
                }

                LastError = null;
                return (true, "SIM description updated in Flickswitch.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"Request failed while calling Flickswitch: {ex.Message}";
                return (false, LastError);
            }
        }

        LastError = authError ?? "Flickswitch authorization failed.";
        return (false, LastError);
    }

    private async Task<(bool ok, string message)> TryRequestSimBalancesAsync(string endpoint, HttpMethod method, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            LastError = "Flickswitch API key is missing.";
            return (false, LastError);
        }

        string? authError = null;

        foreach (var mode in new[] { AuthMode.ApiKeyHeader, AuthMode.ApiKeyLowerHeader, AuthMode.AuthorizationApiKey, AuthMode.Bearer, AuthMode.RawAuthorization })
        {
            var content = method == HttpMethod.Post
                ? new StringContent("{}", Encoding.UTF8, "application/json")
                : null;
            using var request = BuildRequest(method, endpoint, mode, content);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    authError = BuildUnauthorizedError(response.StatusCode, mode, body);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LastError = FormatHttpError(response.StatusCode, body);
                    return (false, LastError);
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object
                            && TryGetPropertyIgnoreCase(doc.RootElement, "message", out var messageElement)
                            && messageElement.ValueKind == JsonValueKind.String)
                        {
                            var message = messageElement.GetString();
                            if (!string.IsNullOrWhiteSpace(message))
                                return (true, message.Trim());
                        }
                    }
                    catch
                    {
                        // Ignore parse errors for non-standard responses.
                    }
                }

                return (true, "Balance refresh requested.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"Request failed while calling Flickswitch: {ex.Message}";
                return (false, LastError);
            }
        }

        LastError = authError ?? "Flickswitch authorization failed.";
        return (false, LastError);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string endpoint, AuthMode mode, HttpContent? content = null)
    {
        var token = _apiKey?.Trim() ?? string.Empty;
        var request = new HttpRequestMessage(method, endpoint);
        if (content != null)
            request.Content = content;

        switch (mode)
        {
            case AuthMode.ApiKeyHeader:
                request.Headers.TryAddWithoutValidation("X-API-Key", token);
                break;

            case AuthMode.ApiKeyLowerHeader:
                request.Headers.TryAddWithoutValidation("apikey", token);
                break;

            case AuthMode.AuthorizationApiKey:
                request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", token);
                break;

            case AuthMode.Bearer:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;

            case AuthMode.RawAuthorization:
                request.Headers.TryAddWithoutValidation("authorization", token);
                break;
        }

        return request;
    }

    private static string BuildUnauthorizedError(HttpStatusCode statusCode, AuthMode mode, string body)
    {
        var serverError = ExtractErrorMessage(body);
        return string.IsNullOrWhiteSpace(serverError)
            ? $"HTTP {(int)statusCode} ({mode} auth mode)"
            : $"HTTP {(int)statusCode}: {serverError} ({mode} auth mode)";
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var error = GetStringIgnoreCase(root, "error", "message", "detail");
            if (!string.IsNullOrWhiteSpace(error))
                return error.Trim();
        }
        catch
        {
            // Fallback to plain text response below.
        }

        var text = body.Trim();
        if (text.Length > 220)
            text = text[..220];

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static HttpContent BuildUpdateDescriptionContent(string description)
    {
        var payload = JsonSerializer.Serialize(new { description });
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static bool TryBuildUpdateIdentifier(
        string? iccid,
        string? phoneNumber,
        string? imsi,
        out string identifierName,
        out string identifierValue,
        out string validationError)
    {
        var hasAnyInput = !string.IsNullOrWhiteSpace(iccid)
                          || !string.IsNullOrWhiteSpace(phoneNumber)
                          || !string.IsNullOrWhiteSpace(imsi);

        var normalizedIccid = NormalizeDigits(iccid);
        if (!string.IsNullOrWhiteSpace(normalizedIccid) && IsValidIccid(normalizedIccid))
        {
            identifierName = "iccid";
            identifierValue = normalizedIccid;
            validationError = string.Empty;
            return true;
        }

        var normalizedMsisdn = NormalizeMsisdn(phoneNumber);
        if (!string.IsNullOrWhiteSpace(normalizedMsisdn) && IsValidMsisdn(normalizedMsisdn))
        {
            identifierName = "msisdn";
            identifierValue = normalizedMsisdn;
            validationError = string.Empty;
            return true;
        }

        var normalizedImsi = NormalizeDigits(imsi);
        if (!string.IsNullOrWhiteSpace(normalizedImsi) && IsValidImsi(normalizedImsi))
        {
            identifierName = "imsi";
            identifierValue = normalizedImsi;
            validationError = string.Empty;
            return true;
        }

        identifierName = string.Empty;
        identifierValue = string.Empty;
        validationError = hasAnyInput
            ? "No valid SIM identifier was provided. ICCID must be 18-22 digits, SIM Number must be 10-15 digits, or IMSI must be 14-16 digits."
            : "ICCID, SIM Number, or IMSI is required.";
        return false;
    }

    private static string FormatHttpError(HttpStatusCode statusCode, string body)
    {
        var trimmedBody = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : $" {body.Trim()}";

        return $"HTTP {(int)statusCode}:{trimmedBody}".Trim();
    }

    private static List<FlickswitchSimInfo> ParseSims(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseSims(doc.RootElement);
    }

    private static List<FlickswitchSimInfo> ParseSims(JsonElement root)
    {
        var sims = new List<FlickswitchSimInfo>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                AddSimFromElement(sims, item);
            }

            return sims;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return sims;

        if (TryGetPropertyIgnoreCase(root, "data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElement.EnumerateArray())
            {
                AddSimFromElement(sims, item);
            }

            return sims;
        }

        AddSimFromElement(sims, root);
        return sims;
    }

    private static void AddSimFromElement(ICollection<FlickswitchSimInfo> sims, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        var info = new FlickswitchSimInfo
        {
            Iccid = GetStringIgnoreCase(element, "iccid", "icc_id", "sim_iccid", "sim_serial", "sim_serial_number"),
            SimNumber = GetStringIgnoreCase(element, "msisdn", "phone", "mobile", "cellphone", "number", "sim_number", "sim_msisdn"),
            Imsi = GetStringIgnoreCase(element, "imsi"),
            Status = GetStringIgnoreCase(element, "status", "state"),
            NetworkStatus = GetStringIgnoreCase(element, "network_status"),
            AirtimeBalance = GetDecimalIgnoreCase(element, "airtime_balance"),
            DataBalanceMb = GetDecimalIgnoreCase(element, "data_balance_in_mb", "data_balance_mb"),
            SmsBalance = GetDecimalIgnoreCase(element, "sms_balance"),
            LastBalanceCheckAt = GetDateTimeOffsetIgnoreCase(element, "last_balance_check")
        };

        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetPropertyIgnoreCase(element, "tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String)
                    continue;

                var value = tag.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    rules.Add(value.Trim());
            }
        }

        foreach (var extracted in ExtractRules(element))
        {
            rules.Add(extracted);
        }

        if (rules.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(info.Status))
                rules.Add($"Status: {info.Status}");

            if (!string.IsNullOrWhiteSpace(info.NetworkStatus))
                rules.Add($"Network: {info.NetworkStatus}");
        }

        info.Rules = rules.OrderBy(x => x).ToList();

        if (string.IsNullOrWhiteSpace(info.Iccid) && string.IsNullOrWhiteSpace(info.SimNumber))
            return;

        sims.Add(info);
    }

    private static FlickswitchSimInfo? SelectBestMatch(IEnumerable<FlickswitchSimInfo> sims, string normalizedIccid, string normalizedPhone)
    {
        FlickswitchSimInfo? best = null;
        var bestScore = 0;

        foreach (var sim in sims)
        {
            var score = ScoreMatch(sim, normalizedIccid, normalizedPhone);
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = sim;
        }

        return best;
    }

    private static int ScoreMatch(FlickswitchSimInfo sim, string normalizedIccid, string normalizedPhone)
    {
        var simIccid = NormalizeDigits(sim.Iccid);
        var simPhone = NormalizeDigits(sim.SimNumber);

        if (!string.IsNullOrWhiteSpace(normalizedIccid))
        {
            if (string.Equals(simIccid, normalizedIccid, StringComparison.Ordinal))
                return 300;
            if (!string.IsNullOrWhiteSpace(simIccid) && simIccid.Contains(normalizedIccid, StringComparison.Ordinal))
                return 220;
        }

        if (!string.IsNullOrWhiteSpace(normalizedPhone))
        {
            if (string.Equals(simPhone, normalizedPhone, StringComparison.Ordinal))
                return 290;
            if (!string.IsNullOrWhiteSpace(simPhone) && simPhone.Contains(normalizedPhone, StringComparison.Ordinal))
                return 210;
        }

        if (!string.IsNullOrWhiteSpace(sim.Iccid) || !string.IsNullOrWhiteSpace(sim.SimNumber))
            return 100;

        return 0;
    }

    private static List<string> ExtractRules(JsonElement element)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Contains("rule", StringComparison.OrdinalIgnoreCase))
                continue;

            ExtractRuleValues(property.Value, values);
        }

        return values.Where(v => !string.IsNullOrWhiteSpace(v)).OrderBy(v => v).ToList();
    }

    private static void ExtractRuleValues(JsonElement value, ISet<string> collector)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    collector.Add(text.Trim());
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    ExtractRuleValues(item, collector);
                }
                break;
            case JsonValueKind.Object:
            {
                var ruleName = GetStringIgnoreCase(value, "name", "rule_name", "title", "code");
                if (!string.IsNullOrWhiteSpace(ruleName))
                {
                    collector.Add(ruleName.Trim());
                    break;
                }

                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name.Contains("name", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("rule", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("title", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("code", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractRuleValues(property.Value, collector);
                    }
                }
                break;
            }
        }
    }

    private static List<string> BuildSearchTerms(string? value, bool includeDigits)
    {
        var terms = new List<string>();
        AddUniqueTerm(terms, value);

        if (includeDigits)
        {
            var digits = NormalizeDigits(value);
            AddUniqueTerm(terms, digits);
        }

        return terms;
    }

    private static void AddUniqueTerm(ICollection<string> terms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var trimmed = value.Trim();
        if (terms.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;

        terms.Add(trimmed);
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "https://app.simcontrol.co.za";

        var trimmed = baseUrl.Trim();
        if (!trimmed.StartsWith("http://", true, CultureInfo.InvariantCulture)
            && !trimmed.StartsWith("https://", true, CultureInfo.InvariantCulture))
        {
            trimmed = "https://" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeMsisdn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("+", StringComparison.Ordinal))
            return "+" + new string(trimmed.Skip(1).Where(char.IsDigit).ToArray());

        return new string(trimmed.Where(char.IsDigit).ToArray());
    }

    private static bool IsValidIccid(string value)
    {
        return value.Length is >= 18 and <= 22;
    }

    private static bool IsValidMsisdn(string value)
    {
        var digits = NormalizeDigits(value);
        return digits.Length is >= 10 and <= 15;
    }

    private static bool IsValidImsi(string value)
    {
        return value.Length is >= 14 and <= 16;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringIgnoreCase(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return null;
    }

    private static decimal? GetDecimalIgnoreCase(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffsetIgnoreCase(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
