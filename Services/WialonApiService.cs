using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class WialonReport
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public string? UnitType { get; set; }
    public string? UniqueId { get; set; }
    public string Code { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Registration { get; set; }
    public string? FleetNumber { get; set; }
    public string? Colour { get; set; }
    public string? VinNumber { get; set; }
    public string? TrackingUnitMake { get; set; }
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Location { get; set; } = "";
    public DateTime? LastUpdateAt { get; set; }
    public string Status { get; set; } = "";
    public string Url { get; set; } = "";
    public int AccountId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class WialonUnitSyncResult
{
    public bool IsSuccess { get; init; }
    public bool CreatedNewUnit { get; init; }
    public long? UnitId { get; init; }
    public string Message { get; init; } = "";
}

public class WialonApiService
{
    private const long UnitSearchFlags = 8398087; // Base unit fields + profile fields (pflds)
    private const int SearchBatchSize = 200;
    // Wialon docs: user/update_item_access
    private const long FullItemAccessMask = 0x0FFFFFFFFFFFFFFFL;
    // Read-only mask: common view rights + unit view rights (connectivity, service intervals, commands).
    private const long ReadOnlyUnitAccessMask =
        0x0000000000000001L |
        0x0000000000000002L |
        0x0000000000000020L |
        0x0000000000000200L |
        0x0000000000001000L |
        0x0000000000004000L |
        0x0000000004000000L |
        0x0000000010000000L |
        0x0000000400000000L;

    private static readonly string[] FullAccessAccounts = { "Executive Account", "Capital Air" };
    private static readonly string[] ReadOnlyAccounts = { "Recovery Controller", "Sindi Mntambo" };
    private static readonly Regex HardwareUnitTypeRegex = new(@"\b(FM[A-Z])\s*[-]?\s*(\d{2,5})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private sealed record WialonResource(long Id, string Name);
    private sealed record WialonUser(long Id, string Name, long BillingAccountId);
    private sealed record WialonUnitLookup(long Id, string Name, long HardwareTypeId, long BillingAccountId, string? UniqueId);
    private sealed class DuplicateUniqueIdConflictException : Exception
    {
        public DuplicateUniqueIdConflictException(string uniqueId)
            : base($"IMEI {uniqueId} already exists in Wialon.")
        {
            UniqueId = uniqueId;
        }

        public string UniqueId { get; }
    }

    private readonly string _baseUrl = "https://hst-api.wialon.eu/";
    private readonly string _token;
    private readonly HttpClient _httpClient;
    private string? _sessionId;
    private int _userId;  // User ID needed for geocoding API
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _geocodeCache = new();
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

    private async Task FetchUserIdAsync()
    {
        // Best-effort fetch of userId; for now leave as no-op if session missing
        try
        {
            if (string.IsNullOrEmpty(_sessionId))
                return;
            // Simple placeholder: do nothing for now
            await Task.CompletedTask;
        }
        catch
        {
            // Ignore
        }
    }

    private async Task<string?> ResolveAddressFromWialonAsync(double lat, double lon)
    {
        // Placeholder implementation - return null to allow fallback to external geocode
        await Task.CompletedTask;
        return null;
    }

    private static string? GetCustomFieldValue(JsonElement element, params string[] candidates)
    {
        try
        {
            foreach (var name in candidates)
            {
                if (element.TryGetProperty(name, out var el))
                {
                    if (el.ValueKind == JsonValueKind.String)
                        return el.GetString();
                    if (el.ValueKind == JsonValueKind.Number || el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                        return el.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static (string? Make, string? Model) ExtractMakeModel(JsonElement item)
    {
        var makeCandidates = new[]
        {
            "make", "brand", "manufacturer", "vehicle_make", "vehicle make", "car_make", "truck_make"
        };
        var modelCandidates = new[]
        {
            "model", "vehicle_model", "vehicle model", "car_model", "truck_model", "type"
        };
        var makeModelCandidates = new[]
        {
            "make & model", "make and model", "make/model", "makemodel", "brand model"
        };

        string? make = GetCustomFieldValue(item, makeCandidates);
        string? model = GetCustomFieldValue(item, modelCandidates);

        // Custom property bag values (prp) when available.
        make ??= GetNestedFieldValue(item, "prp", makeCandidates);
        model ??= GetNestedFieldValue(item, "prp", modelCandidates);

        // Wialon profile fields.
        make ??= GetNestedFieldValue(item, "pflds", makeCandidates);
        model ??= GetNestedFieldValue(item, "pflds", modelCandidates);

        // Generic fields/admin fields as fallback.
        make ??= GetNestedFieldValue(item, "flds", makeCandidates);
        model ??= GetNestedFieldValue(item, "flds", modelCandidates);
        make ??= GetNestedFieldValue(item, "aflds", makeCandidates);
        model ??= GetNestedFieldValue(item, "aflds", modelCandidates);

        var makeModelCombined =
            GetNestedFieldValue(item, "pflds", makeModelCandidates) ??
            GetNestedFieldValue(item, "flds", makeModelCandidates) ??
            GetNestedFieldValue(item, "aflds", makeModelCandidates);

        if (!string.IsNullOrWhiteSpace(makeModelCombined))
        {
            FillFromCombinedMakeModel(makeModelCombined!, ref make, ref model);
        }

        return (NormalizeOutput(make), NormalizeOutput(model));
    }

    private static string? GetNestedFieldValue(JsonElement item, string bucketName, params string[] candidates)
    {
        if (!item.TryGetProperty(bucketName, out var bucket))
            return null;

        // Direct object keys (e.g. prp values)
        if (bucket.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in bucket.EnumerateObject())
            {
                if (FieldNameMatches(prop.Name, candidates))
                {
                    var directValue = ReadFieldValue(prop.Value);
                    if (!string.IsNullOrWhiteSpace(directValue))
                        return directValue;
                }
            }

            // Indexed entries where each element has n/v (e.g. pflds, flds, aflds)
            foreach (var prop in bucket.EnumerateObject())
            {
                var nestedValue = GetNamedEntryValue(prop.Value, candidates);
                if (!string.IsNullOrWhiteSpace(nestedValue))
                    return nestedValue;
            }
        }

        if (bucket.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in bucket.EnumerateArray())
            {
                var value = GetNamedEntryValue(entry, candidates);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string? GetNamedEntryValue(JsonElement entry, params string[] candidates)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return null;

        if (!entry.TryGetProperty("n", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return null;

        var fieldName = nameElement.GetString();
        if (!FieldNameMatches(fieldName, candidates))
            return null;

        if (!entry.TryGetProperty("v", out var valueElement))
            return null;

        return ReadFieldValue(valueElement);
    }

    private static string? ReadFieldValue(JsonElement valueElement)
    {
        return valueElement.ValueKind switch
        {
            JsonValueKind.String => valueElement.GetString(),
            JsonValueKind.Number => valueElement.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool FieldNameMatches(string? actual, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(actual))
            return false;

        var normalizedActual = NormalizeFieldName(actual);
        foreach (var candidate in candidates)
        {
            if (normalizedActual == NormalizeFieldName(candidate))
                return true;
        }

        return false;
    }

    private static string NormalizeFieldName(string name)
    {
        return new string(name
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static void FillFromCombinedMakeModel(string combined, ref string? make, ref string? model)
    {
        var text = combined.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (string.IsNullOrWhiteSpace(make) && string.IsNullOrWhiteSpace(model))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                make = parts[0];
                model = parts[1];
            }
            else
            {
                make = text;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(make))
            make = text;

        if (string.IsNullOrWhiteSpace(model))
        {
            if (!string.IsNullOrWhiteSpace(make) &&
                text.StartsWith(make + " ", StringComparison.OrdinalIgnoreCase))
            {
                model = text.Substring(make.Length).Trim();
            }
            else
            {
                model = text;
            }
        }
    }

    private static string? NormalizeOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string? ExtractUnitField(JsonElement item, params string[] candidates)
    {
        var value =
            GetCustomFieldValue(item, candidates) ??
            GetNestedFieldValue(item, "prp", candidates) ??
            GetNestedFieldValue(item, "pflds", candidates) ??
            GetNestedFieldValue(item, "flds", candidates) ??
            GetNestedFieldValue(item, "aflds", candidates);

        return NormalizeOutput(value);
    }

    private static string? ExtractUnitType(JsonElement item, IReadOnlyDictionary<long, string> hardwareTypes)
    {
        if (item.TryGetProperty("hw", out var hardwareElement) &&
            hardwareElement.TryGetInt64(out var hardwareId) &&
            hardwareId > 0 &&
            hardwareTypes.TryGetValue(hardwareId, out var hardwareName) &&
            !string.IsNullOrWhiteSpace(hardwareName))
        {
            return NormalizeOutput(FormatHardwareUnitType(hardwareName));
        }

        var unitTypeCandidates = new[]
        {
            "unit_type", "unit type",
            "tracker_type", "tracker type",
            "device_type", "device type",
            "hardware_type", "hardware type",
            "tracker_model", "tracker model"
        };

        var unitType =
            GetCustomFieldValue(item, unitTypeCandidates) ??
            GetNestedFieldValue(item, "prp", unitTypeCandidates) ??
            GetNestedFieldValue(item, "pflds", unitTypeCandidates) ??
            GetNestedFieldValue(item, "flds", unitTypeCandidates) ??
            GetNestedFieldValue(item, "aflds", unitTypeCandidates);

        return NormalizeOutput(unitType);
    }

    private static string FormatHardwareUnitType(string hardwareName)
    {
        var normalized = hardwareName.Trim();
        var match = HardwareUnitTypeRegex.Match(normalized);
        if (match.Success)
        {
            return $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}";
        }

        return normalized;
    }

    private static string ExtractUniqueId(JsonElement item, int unitId)
    {
        if (item.TryGetProperty("uid", out var uidElement) && uidElement.ValueKind == JsonValueKind.String)
        {
            var uid = uidElement.GetString();
            if (!string.IsNullOrWhiteSpace(uid))
                return uid.Trim();
        }

        if (item.TryGetProperty("uid2", out var uid2Element) && uid2Element.ValueKind == JsonValueKind.String)
        {
            var uid2 = uid2Element.GetString();
            if (!string.IsNullOrWhiteSpace(uid2))
                return uid2.Trim();
        }

        if (item.TryGetProperty("hw", out var hwElement) && hwElement.TryGetInt64(out var hardwareId) && hardwareId > 0)
        {
            return hardwareId.ToString(CultureInfo.InvariantCulture);
        }

        return unitId.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildCode(string? unitType, string uniqueId)
    {
        if (!string.IsNullOrWhiteSpace(unitType) && !string.IsNullOrWhiteSpace(uniqueId))
            return $"{unitType} | {uniqueId}";

        if (!string.IsNullOrWhiteSpace(unitType))
            return unitType;

        return uniqueId;
    }

    public async Task<WialonUnitSyncResult> SyncJobCardUnitAsync(JobCard jobCard)
    {
        if (jobCard is null)
        {
            return new WialonUnitSyncResult
            {
                IsSuccess = false,
                Message = "No job card was provided for Wialon sync."
            };
        }

        if (string.IsNullOrEmpty(_sessionId))
        {
            return new WialonUnitSyncResult
            {
                IsSuccess = false,
                Message = "Not connected to Wialon."
            };
        }

        try
        {
            var normalizedImei = NormalizeImei(jobCard.Imei);
            if (string.IsNullOrWhiteSpace(normalizedImei))
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = "IMEI is required to create or update a Wialon unit."
                };
            }

            var company = PrepareFieldValue(jobCard.Company);
            if (string.IsNullOrWhiteSpace(company))
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = "Company is required to match a Wialon account."
                };
            }

            var resources = await GetResourcesInternalAsync();
            var matchedResource = FindBestResourceMatch(resources, company);
            if (matchedResource is null)
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = $"Could not find Wialon account '{company}'."
                };
            }

            var users = await GetUsersInternalAsync();
            var creatorUser = FindBestUserForResource(users, matchedResource.Id, company)
                              ?? users.FirstOrDefault(u => u.Id == _userId);
            if (creatorUser is null || creatorUser.Id <= 0)
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = $"Could not find a Wialon user for account '{matchedResource.Name}'."
                };
            }

            var existingUnit = await FindUnitByImeiAsync(normalizedImei);
            if (existingUnit is not null &&
                existingUnit.BillingAccountId > 0 &&
                existingUnit.BillingAccountId != matchedResource.Id)
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = $"IMEI {normalizedImei} already exists on another account in Wialon."
                };
            }

            var hardwareTypeId = await ResolveHardwareTypeIdAsync(jobCard.TrackingUnitMake, existingUnit?.HardwareTypeId ?? 0);
            if (hardwareTypeId <= 0)
            {
                return new WialonUnitSyncResult
                {
                    IsSuccess = false,
                    Message = $"Could not match tracking unit make '{jobCard.TrackingUnitMake}' to a Wialon hardware type."
                };
            }

            var unitName = BuildUnitName(jobCard);
            var createdNewUnit = false;
            long unitId;

            if (existingUnit is null)
            {
                unitId = await CreateUnitAsync(creatorUser.Id, unitName, hardwareTypeId);
                createdNewUnit = true;
            }
            else
            {
                unitId = existingUnit.Id;
                await TryUpdateUnitNameAsync(unitId, unitName);
            }

            var existingNormalizedImei = NormalizeImei(existingUnit?.UniqueId);
            var identityAlreadyMatches =
                existingUnit is not null &&
                existingUnit.HardwareTypeId == hardwareTypeId &&
                string.Equals(existingNormalizedImei, normalizedImei, StringComparison.Ordinal);

            if (!identityAlreadyMatches)
            {
                unitId = await UpdateUnitIdentityWithRecoveryAsync(
                    unitId,
                    hardwareTypeId,
                    normalizedImei,
                    createdNewUnit,
                    matchedResource.Id);
            }

            await UpdateProfileFieldsAsync(unitId, jobCard);
            await UpsertCustomFieldsAsync(unitId, jobCard);
            await ApplyDefaultUnitAccessPoliciesAsync(unitId, users, resources);

            var action = createdNewUnit ? "created" : "updated";
            return new WialonUnitSyncResult
            {
                IsSuccess = true,
                CreatedNewUnit = createdNewUnit,
                UnitId = unitId,
                Message = $"Wialon unit {action} under account '{matchedResource.Name}'."
            };
        }
        catch (DuplicateUniqueIdConflictException ex)
        {
            return new WialonUnitSyncResult
            {
                IsSuccess = true,
                CreatedNewUnit = false,
                UnitId = null,
                Message = $"IMEI {ex.UniqueId} is already loaded in Wialon. Skipped duplicate unit creation."
            };
        }
        catch (Exception ex)
        {
            return new WialonUnitSyncResult
            {
                IsSuccess = false,
                Message = $"Wialon sync failed: {ex.Message}"
            };
        }
    }

    private async Task<JsonElement> ExecuteApiAsync(string service, object parameters)
    {
        if (string.IsNullOrEmpty(_sessionId))
            throw new Exception("Not connected to Wialon. Please connect first.");

        var paramsJson = JsonSerializer.Serialize(parameters);
        var url = $"{_baseUrl}wialon/ajax.html?svc={service}&params={Uri.EscapeDataString(paramsJson)}&sid={_sessionId}";
        var response = await _httpClient.GetAsync(url);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");

        if (string.IsNullOrWhiteSpace(responseContent))
            throw new Exception($"Empty response from Wialon for {service}.");

        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement.Clone();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errorElement))
        {
            var errorText = errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : errorElement.ToString();
            throw new Exception($"{service} returned error {errorText}");
        }

        return root;
    }

    private async Task<JsonElement> SearchItemsAsync(
        string itemsType,
        string propName,
        string propValueMask,
        long flags,
        int from = 0,
        int to = SearchBatchSize - 1)
    {
        var searchParams = new
        {
            spec = new
            {
                itemsType,
                propName,
                propValueMask,
                sortType = "sys_name"
            },
            force = 1,
            flags,
            from,
            to
        };

        return await ExecuteApiAsync("core/search_items", searchParams);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement searchResult)
    {
        if (searchResult.ValueKind != JsonValueKind.Object)
            yield break;

        if (!searchResult.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in items.EnumerateArray())
            yield return item;
    }

    private async Task<List<WialonResource>> GetResourcesInternalAsync()
    {
        var resources = new List<WialonResource>();
        var from = 0;

        while (true)
        {
            var searchResult = await SearchItemsAsync(
                itemsType: "avl_resource",
                propName: "sys_name",
                propValueMask: "*",
                flags: 1,
                from: from,
                to: from + SearchBatchSize - 1);

            var batch = EnumerateItems(searchResult).ToList();
            foreach (var item in batch)
            {
                if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0)
                    continue;
                if (!item.TryGetProperty("nm", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                    continue;

                var name = PrepareFieldValue(nameElement.GetString());
                if (!string.IsNullOrWhiteSpace(name))
                    resources.Add(new WialonResource(id, name));
            }

            var totalCount = searchResult.TryGetProperty("totalItemsCount", out var totalElement) && totalElement.TryGetInt32(out var total)
                ? total
                : 0;

            if (batch.Count == 0)
                break;

            from += batch.Count;

            if (totalCount > 0 && from >= totalCount)
                break;

            if (batch.Count < SearchBatchSize)
                break;
        }

        return resources;
    }

    private async Task<List<WialonUser>> GetUsersInternalAsync()
    {
        var users = new List<WialonUser>();
        var from = 0;

        while (true)
        {
            var searchResult = await SearchItemsAsync(
                itemsType: "user",
                propName: "sys_name",
                propValueMask: "*",
                flags: 7,
                from: from,
                to: from + SearchBatchSize - 1);

            var batch = EnumerateItems(searchResult).ToList();
            foreach (var item in batch)
            {
                if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0)
                    continue;
                if (!item.TryGetProperty("nm", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                    continue;

                var name = PrepareFieldValue(nameElement.GetString()) ?? $"User {id}";
                var billingAccountId = item.TryGetProperty("bact", out var bactElement) && bactElement.TryGetInt64(out var bact)
                    ? bact
                    : 0;

                users.Add(new WialonUser(id, name, billingAccountId));
            }

            var totalCount = searchResult.TryGetProperty("totalItemsCount", out var totalElement) && totalElement.TryGetInt32(out var total)
                ? total
                : 0;

            if (batch.Count == 0)
                break;

            from += batch.Count;

            if (totalCount > 0 && from >= totalCount)
                break;

            if (batch.Count < SearchBatchSize)
                break;
        }

        return users;
    }

    private static WialonResource? FindBestResourceMatch(IEnumerable<WialonResource> resources, string companyName)
    {
        var company = PrepareFieldValue(companyName);
        if (string.IsNullOrWhiteSpace(company))
            return null;

        var list = resources.ToList();
        if (list.Count == 0)
            return null;

        var exact = list.FirstOrDefault(r => string.Equals(r.Name, company, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var normalizedCompany = NormalizeComparableText(company);
        exact = list.FirstOrDefault(r => NormalizeComparableText(r.Name) == normalizedCompany);
        if (exact is not null)
            return exact;

        var contains = list.FirstOrDefault(r =>
            NormalizeComparableText(r.Name).Contains(normalizedCompany, StringComparison.OrdinalIgnoreCase)
            || normalizedCompany.Contains(NormalizeComparableText(r.Name), StringComparison.OrdinalIgnoreCase));
        return contains;
    }

    private static WialonUser? FindBestUserForResource(IEnumerable<WialonUser> users, long resourceId, string companyName)
    {
        var candidates = users.Where(u => u.BillingAccountId == resourceId).ToList();
        if (candidates.Count == 0)
            return null;

        var exact = candidates.FirstOrDefault(u => string.Equals(u.Name, companyName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var normalizedCompany = NormalizeComparableText(companyName);
        exact = candidates.FirstOrDefault(u => NormalizeComparableText(u.Name) == normalizedCompany);
        if (exact is not null)
            return exact;

        return candidates[0];
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private async Task ApplyDefaultUnitAccessPoliciesAsync(
        long unitId,
        IReadOnlyCollection<WialonUser> users,
        IReadOnlyCollection<WialonResource> resources)
    {
        var missingAccounts = new List<string>();
        var policies = new List<(string AccountName, long AccessMask)>
        {
            (FullAccessAccounts[0], FullItemAccessMask),
            (FullAccessAccounts[1], FullItemAccessMask),
            (ReadOnlyAccounts[0], ReadOnlyUnitAccessMask),
            (ReadOnlyAccounts[1], ReadOnlyUnitAccessMask)
        };

        foreach (var policy in policies)
        {
            var user = FindUserForAccessPolicy(users, resources, policy.AccountName);
            if (user is null)
            {
                missingAccounts.Add(policy.AccountName);
                continue;
            }

            await ExecuteApiAsync("user/update_item_access", new
            {
                userId = user.Id,
                itemId = unitId,
                accessMask = policy.AccessMask
            });
        }

        if (missingAccounts.Count > 0)
        {
            throw new Exception($"Could not find Wialon users/accounts for access policy: {string.Join(", ", missingAccounts)}.");
        }
    }

    private static WialonUser? FindUserForAccessPolicy(
        IEnumerable<WialonUser> users,
        IEnumerable<WialonResource> resources,
        string accountOrUserName)
    {
        var userList = users.ToList();
        if (userList.Count == 0 || string.IsNullOrWhiteSpace(accountOrUserName))
            return null;

        var exactUser = userList.FirstOrDefault(u => string.Equals(u.Name, accountOrUserName, StringComparison.OrdinalIgnoreCase));
        if (exactUser is not null)
            return exactUser;

        var normalizedName = NormalizeComparableText(accountOrUserName);
        exactUser = userList.FirstOrDefault(u => NormalizeComparableText(u.Name) == normalizedName);
        if (exactUser is not null)
            return exactUser;

        var matchedResource = FindBestResourceMatch(resources, accountOrUserName);
        if (matchedResource is not null)
        {
            var usersInResource = userList.Where(u => u.BillingAccountId == matchedResource.Id).ToList();
            if (usersInResource.Count > 0)
            {
                return usersInResource.FirstOrDefault(u => NormalizeComparableText(u.Name) == normalizedName)
                    ?? usersInResource.FirstOrDefault();
            }
        }

        return userList.FirstOrDefault(u =>
            NormalizeComparableText(u.Name).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains(NormalizeComparableText(u.Name), StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildUnitName(JobCard jobCard)
    {
        var registration = PrepareFieldValue(jobCard.Registration);
        var fleetNumber = PrepareFieldValue(jobCard.FleetNumber);
        var company = PrepareFieldValue(jobCard.Company);
        var imei = NormalizeImei(jobCard.Imei);

        var name = registration;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(fleetNumber))
            name = fleetNumber;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(imei))
            name = $"{company} {imei}";
        if (string.IsNullOrWhiteSpace(name))
            name = !string.IsNullOrWhiteSpace(imei) ? $"Unit {imei}" : "New Unit";

        if (!string.IsNullOrWhiteSpace(fleetNumber) && !string.Equals(name, fleetNumber, StringComparison.OrdinalIgnoreCase))
            name = $"{name} ({fleetNumber})";

        name = name.Trim();
        if (name.Length < 4)
            name = $"{name} unit";
        if (name.Length > 50)
            name = name.Substring(0, 50).Trim();

        return name;
    }

    private async Task<WialonUnitLookup?> FindUnitByImeiAsync(string normalizedImei)
    {
        var masks = new[]
        {
            normalizedImei,
            $"*{normalizedImei}*"
        };

        foreach (var mask in masks)
        {
            var from = 0;
            while (true)
            {
                var searchResult = await SearchItemsAsync(
                    itemsType: "avl_unit",
                    propName: "sys_unique_id",
                    propValueMask: mask,
                    flags: UnitSearchFlags,
                    from: from,
                    to: from + SearchBatchSize - 1);

                var batch = EnumerateItems(searchResult).ToList();
                foreach (var item in batch)
                {
                    var candidate = ParseUnitLookup(item);
                    if (candidate is null)
                        continue;

                    var candidateImei = ExtractUniqueId(item, (int)candidate.Id);
                    if (IsImeiMatch(candidateImei, normalizedImei))
                        return candidate;
                }

                var totalCount = searchResult.TryGetProperty("totalItemsCount", out var totalElement) && totalElement.TryGetInt32(out var total)
                    ? total
                    : 0;

                if (batch.Count == 0)
                    break;

                from += batch.Count;

                if (totalCount > 0 && from >= totalCount)
                    break;

                if (batch.Count < SearchBatchSize)
                    break;
            }
        }

        var fromAll = 0;
        var totalUnits = int.MaxValue;
        while (fromAll < totalUnits)
        {
            var searchResult = await SearchItemsAsync(
                itemsType: "avl_unit",
                propName: "sys_name",
                propValueMask: "*",
                flags: UnitSearchFlags,
                from: fromAll,
                to: fromAll + SearchBatchSize - 1);

            if (searchResult.TryGetProperty("totalItemsCount", out var totalElement) &&
                totalElement.TryGetInt32(out var parsedTotal) &&
                parsedTotal > 0)
            {
                totalUnits = parsedTotal;
            }

            var batch = EnumerateItems(searchResult).ToList();
            foreach (var item in batch)
            {
                var candidate = ParseUnitLookup(item);
                if (candidate is null)
                    continue;

                var candidateImei = ExtractUniqueId(item, (int)candidate.Id);
                if (IsImeiMatch(candidateImei, normalizedImei))
                    return candidate;
            }

            if (batch.Count == 0)
                break;

            fromAll += batch.Count;

            if (batch.Count < SearchBatchSize)
                break;
        }

        return null;
    }

    private static WialonUnitLookup? ParseUnitLookup(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0)
            return null;

        var name = item.TryGetProperty("nm", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? (nameElement.GetString() ?? $"Unit {id}")
            : $"Unit {id}";

        var hardwareTypeId = item.TryGetProperty("hw", out var hwElement) && hwElement.TryGetInt64(out var hw)
            ? hw
            : 0;

        var billingAccountId = item.TryGetProperty("bact", out var bactElement) && bactElement.TryGetInt64(out var bact)
            ? bact
            : 0;

        var uniqueId = ExtractUniqueId(item, (int)id);
        return new WialonUnitLookup(id, name, hardwareTypeId, billingAccountId, uniqueId);
    }

    private async Task<long> CreateUnitAsync(long creatorUserId, string unitName, long hardwareTypeId)
    {
        var result = await ExecuteApiAsync("core/create_unit", new
        {
            creatorId = creatorUserId,
            name = unitName,
            hwTypeId = hardwareTypeId,
            dataFlags = UnitSearchFlags
        });

        if (result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("item", out var itemElement) &&
                itemElement.ValueKind == JsonValueKind.Object &&
                itemElement.TryGetProperty("id", out var itemIdElement) &&
                itemIdElement.TryGetInt64(out var itemId) &&
                itemId > 0)
            {
                return itemId;
            }

            if (result.TryGetProperty("id", out var idElement) &&
                idElement.TryGetInt64(out var id) &&
                id > 0)
            {
                return id;
            }
        }

        throw new Exception("Wialon did not return a valid unit ID after creation.");
    }

    private async Task UpdateUnitIdentityAsync(long unitId, long hardwareTypeId, string uniqueId)
    {
        await ExecuteApiAsync("unit/update_device_type", new
        {
            itemId = unitId,
            deviceTypeId = hardwareTypeId,
            uniqueId
        });
    }

    private async Task<long> UpdateUnitIdentityWithRecoveryAsync(
        long unitId,
        long hardwareTypeId,
        string uniqueId,
        bool createdNewUnit,
        long expectedAccountId)
    {
        try
        {
            await UpdateUnitIdentityAsync(unitId, hardwareTypeId, uniqueId);
            return unitId;
        }
        catch (Exception ex) when (IsDuplicateUniqueIdError(ex))
        {
            var duplicateUnit = await FindUnitByImeiAsync(uniqueId);
            if (duplicateUnit is null)
            {
                if (createdNewUnit)
                {
                    await TryDeleteUnitAsync(unitId);
                }

                throw new DuplicateUniqueIdConflictException(uniqueId);
            }

            if (duplicateUnit.BillingAccountId > 0 &&
                expectedAccountId > 0 &&
                duplicateUnit.BillingAccountId != expectedAccountId)
            {
                throw new Exception($"IMEI {uniqueId} already exists on another account in Wialon.");
            }

            if (createdNewUnit && duplicateUnit.Id != unitId)
            {
                await TryDeleteUnitAsync(unitId);
            }

            return duplicateUnit.Id;
        }
    }

    private static bool IsDuplicateUniqueIdError(Exception ex)
    {
        return ex.Message.Contains("returned error 1002", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("error 1002", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("1002", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryDeleteUnitAsync(long unitId)
    {
        try
        {
            await ExecuteApiAsync("item/delete_item", new { itemId = unitId });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clean up temporary unit {unitId}: {ex.Message}");
        }
    }

    private async Task TryUpdateUnitNameAsync(long unitId, string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName))
            return;

        try
        {
            await ExecuteApiAsync("item/update_name", new
            {
                itemId = unitId,
                name = unitName
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update unit name for {unitId}: {ex.Message}");
        }
    }

    private async Task<long> ResolveHardwareTypeIdAsync(string? trackingUnitMake, long fallbackHardwareTypeId)
    {
        var hardwareTypes = await GetHardwareTypesMapAsync();
        if (hardwareTypes.Count == 0)
            return fallbackHardwareTypeId;

        var fromTrackingMake = FindHardwareTypeByName(hardwareTypes, trackingUnitMake);
        if (fromTrackingMake > 0)
            return fromTrackingMake;

        if (fallbackHardwareTypeId > 0 && hardwareTypes.ContainsKey(fallbackHardwareTypeId))
            return fallbackHardwareTypeId;

        return 0;
    }

    private static long FindHardwareTypeByName(IReadOnlyDictionary<long, string> hardwareTypes, string? trackingUnitMake)
    {
        var value = PrepareFieldValue(trackingUnitMake);
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var normalizedInput = NormalizeComparableText(value);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return 0;

        foreach (var pair in hardwareTypes)
        {
            if (NormalizeComparableText(pair.Value) == normalizedInput)
                return pair.Key;
        }

        foreach (var pair in hardwareTypes)
        {
            var normalizedCandidate = NormalizeComparableText(pair.Value);
            if (normalizedCandidate.Contains(normalizedInput, StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        return 0;
    }

    private async Task UpdateProfileFieldsAsync(long unitId, JobCard jobCard)
    {
        var fields = new Dictionary<string, string?>
        {
            ["registration_plate"] = PrepareFieldValue(jobCard.Registration),
            ["brand"] = PrepareFieldValue(jobCard.Make),
            ["model"] = PrepareFieldValue(jobCard.Model),
            ["vin"] = PrepareFieldValue(jobCard.VinNumber),
            ["color"] = PrepareFieldValue(jobCard.Colour),
            ["vehicle_type"] = PrepareFieldValue(jobCard.Company)
        };

        foreach (var pair in fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
                continue;

            try
            {
                await ExecuteApiAsync("item/update_profile_field", new
                {
                    itemId = unitId,
                    n = pair.Key,
                    v = pair.Value
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update profile field {pair.Key} for unit {unitId}: {ex.Message}");
            }
        }
    }

    private async Task UpsertCustomFieldsAsync(long unitId, JobCard jobCard)
    {
        Dictionary<string, long> existingCustomFieldIds;
        try
        {
            existingCustomFieldIds = await GetCustomFieldIdsByNameAsync(unitId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load custom fields for unit {unitId}: {ex.Message}");
            existingCustomFieldIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var customFields = new Dictionary<string, string?>
        {
            ["Registration"] = PrepareFieldValue(jobCard.Registration),
            ["VIN"] = PrepareFieldValue(jobCard.VinNumber),
            ["Make"] = PrepareFieldValue(jobCard.Make),
            ["Model"] = PrepareFieldValue(jobCard.Model),
            ["Colour"] = PrepareFieldValue(jobCard.Colour)
        };

        foreach (var pair in customFields)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
                continue;

            try
            {
                var normalizedFieldName = NormalizeFieldName(pair.Key);
                existingCustomFieldIds.TryGetValue(normalizedFieldName, out var fieldId);
                var savedFieldId = await UpsertCustomFieldAsync(unitId, fieldId, pair.Key, pair.Value);
                if (savedFieldId.HasValue && savedFieldId.Value > 0)
                    existingCustomFieldIds[normalizedFieldName] = savedFieldId.Value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to upsert custom field '{pair.Key}' for unit {unitId}: {ex.Message}");
            }
        }
    }

    private async Task<Dictionary<string, long>> GetCustomFieldIdsByNameAsync(long unitId)
    {
        var customFieldIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var searchResult = await SearchItemsAsync(
            itemsType: "avl_unit",
            propName: "sys_id",
            propValueMask: unitId.ToString(CultureInfo.InvariantCulture),
            flags: UnitSearchFlags,
            from: 0,
            to: 0);

        foreach (var item in EnumerateItems(searchResult))
        {
            if (!item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id != unitId)
                continue;

            if (!item.TryGetProperty("flds", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Object)
                break;

            foreach (var fieldEntry in fieldsElement.EnumerateObject())
            {
                var field = fieldEntry.Value;
                if (!field.TryGetProperty("id", out var fieldIdElement) || !fieldIdElement.TryGetInt64(out var fieldId) || fieldId <= 0)
                    continue;
                if (!field.TryGetProperty("n", out var fieldNameElement) || fieldNameElement.ValueKind != JsonValueKind.String)
                    continue;

                var fieldName = fieldNameElement.GetString();
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                customFieldIds[NormalizeFieldName(fieldName)] = fieldId;
            }

            break;
        }

        return customFieldIds;
    }

    private async Task<long?> UpsertCustomFieldAsync(long unitId, long fieldId, string fieldName, string fieldValue)
    {
        if (fieldId > 0)
        {
            try
            {
                await ExecuteApiAsync("item/update_custom_field", new
                {
                    itemId = unitId,
                    id = fieldId,
                    callMode = "update",
                    n = fieldName,
                    v = fieldValue
                });
                return fieldId;
            }
            catch
            {
                await ExecuteApiAsync("item/update_custom_field", new
                {
                    itemId = unitId,
                    id = fieldId,
                    callMode = 0,
                    n = fieldName,
                    v = fieldValue
                });
                return fieldId;
            }
        }

        try
        {
            var createResult = await ExecuteApiAsync("item/update_custom_field", new
            {
                itemId = unitId,
                id = 0,
                callMode = "create",
                n = fieldName,
                v = fieldValue
            });
            return ExtractCustomFieldId(createResult);
        }
        catch
        {
            var createResult = await ExecuteApiAsync("item/update_custom_field", new
            {
                itemId = unitId,
                id = 0,
                callMode = 1,
                n = fieldName,
                v = fieldValue
            });
            return ExtractCustomFieldId(createResult);
        }
    }

    private static long? ExtractCustomFieldId(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Number && response.TryGetInt64(out var numericId) && numericId > 0)
            return numericId;

        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("id", out var idElement) &&
            idElement.TryGetInt64(out var idFromObject) &&
            idFromObject > 0)
        {
            return idFromObject;
        }

        if (response.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in response.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt64(out var idFromArray) && idFromArray > 0)
                    return idFromArray;

                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("id", out var idInEntry) &&
                    idInEntry.TryGetInt64(out var objectId) &&
                    objectId > 0)
                {
                    return objectId;
                }
            }
        }

        return null;
    }

    private static string? PrepareFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? BuildRegistrationFleetValue(string? registration, string? fleetNumber)
    {
        var registrationValue = PrepareFieldValue(registration);
        var fleetValue = PrepareFieldValue(fleetNumber);

        if (string.IsNullOrWhiteSpace(registrationValue) && string.IsNullOrWhiteSpace(fleetValue))
            return null;

        if (!string.IsNullOrWhiteSpace(registrationValue) && !string.IsNullOrWhiteSpace(fleetValue))
            return $"{registrationValue} - ({fleetValue})";

        return registrationValue ?? fleetValue;
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

    private async Task<Dictionary<long, string>> GetHardwareTypesMapAsync()
    {
        var hardwareMap = new Dictionary<long, string>();
        if (string.IsNullOrEmpty(_sessionId))
        {
            return hardwareMap;
        }

        try
        {
            var url = $"{_baseUrl}wialon/ajax.html?svc=core/get_hw_types&params=%7B%7D&sid={_sessionId}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
            {
                return hardwareMap;
            }

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return hardwareMap;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || id <= 0)
                {
                    continue;
                }

                if (!element.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                hardwareMap[id] = name.Trim();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load hardware types: {ex.Message}");
        }

        return hardwareMap;
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
            var hardwareTypes = await GetHardwareTypesMapAsync();

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
                flags = UnitSearchFlags,
                from = from,
                to = from + Math.Max(1, batchSize) - 1   // Inclusive upper bound in Wialon API
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
                        var name = item.TryGetProperty("nm", out var nmElement) ? nmElement.GetString() : "Unknown";
                        var id = item.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : 0;

                        // Billing/account mapping
                        var accountId = item.TryGetProperty("bact", out var bactElement) ? bactElement.GetInt32() : 0;
                        var client = accountId > 0 && accountsMap.ContainsKey(accountId) ? accountsMap[accountId] : string.Empty;

                        // Try resource/creator/account group as fallbacks
                        if (string.IsNullOrEmpty(client))
                        {
                            if (item.TryGetProperty("rid", out var ridElement) && ridElement.TryGetInt32(out var ridValue) && accountsMap.TryGetValue(ridValue, out var rName))
                            {
                                accountId = ridValue;
                                client = rName;
                            }
                            else if (item.TryGetProperty("crt", out var crtElement) && crtElement.TryGetInt32(out var crtValue) && accountsMap.TryGetValue(crtValue, out var cName))
                            {
                                accountId = crtValue;
                                client = cName;
                            }
                            else if (item.TryGetProperty("ag", out var agElement) && agElement.TryGetInt32(out var agValue) && accountsMap.TryGetValue(agValue, out var agName))
                            {
                                accountId = agValue;
                                client = agName;
                            }
                        }

                        if (string.IsNullOrEmpty(client))
                            client = name ?? "Unknown";

                        // Location and coordinates
                        var location = string.Empty;
                        double? latValue = null;
                        double? lonValue = null;
                        if (item.TryGetProperty("pos", out var posElement) && posElement.ValueKind == JsonValueKind.Object)
                        {
                            if (posElement.TryGetProperty("a", out var addressElement) && addressElement.ValueKind == JsonValueKind.String)
                            {
                                var address = addressElement.GetString();
                                if (!string.IsNullOrWhiteSpace(address))
                                    location = address!;
                            }

                            if (posElement.TryGetProperty("y", out var latElement) && posElement.TryGetProperty("x", out var lonElement)
                                && latElement.TryGetDouble(out var lat) && lonElement.TryGetDouble(out var lon))
                            {
                                latValue = lat;
                                lonValue = lon;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(location))
                        {
                            location = latValue.HasValue && lonValue.HasValue ? FormattableString.Invariant($"{latValue:F4}, {lonValue:F4}") : "Unknown";
                        }

                        // Timestamps
                        DateTime? lastUpdateAt = null;
                        if (item.TryGetProperty("lmsg", out var lmsgElement))
                        {
                            if (lmsgElement.ValueKind == JsonValueKind.Number && lmsgElement.TryGetInt64(out var lmsgTime) && lmsgTime > 0)
                                lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(lmsgTime).DateTime;
                            else if (lmsgElement.ValueKind == JsonValueKind.Object && lmsgElement.TryGetProperty("t", out var lmsgT) && lmsgT.TryGetInt64(out var lmsgObjTime) && lmsgObjTime > 0)
                                lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(lmsgObjTime).DateTime;
                        }

                        if (lastUpdateAt is null && item.TryGetProperty("pos", out var posForTime) && posForTime.ValueKind == JsonValueKind.Object && posForTime.TryGetProperty("t", out var posT) && posT.TryGetInt64(out var posTime) && posTime > 0)
                        {
                            lastUpdateAt = DateTimeOffset.FromUnixTimeSeconds(posTime).DateTime;
                        }

                        var createdAt = DateTime.Now;
                        if (item.TryGetProperty("ct", out var ctElement) && ctElement.ValueKind == JsonValueKind.Number && ctElement.TryGetInt64(out var ctUnix))
                            createdAt = DateTimeOffset.FromUnixTimeSeconds(ctUnix).DateTime;

                        var (make, model) = ExtractMakeModel(item);
                        var unitType = ExtractUnitType(item, hardwareTypes);
                        var uniqueId = ExtractUniqueId(item, id);
                        var code = BuildCode(unitType, uniqueId);
                        var registration = ExtractUnitField(item, "registration", "reg", "registration_plate", "number_plate");
                        var fleetNumber = ExtractUnitField(item, "fleet", "fleet_number", "fleet no", "fleet_no", "fleetnumber");
                        var colour = ExtractUnitField(item, "colour", "color", "vehicle_colour", "vehicle_color");
                        var vin = ExtractUnitField(item, "vin", "vin_number", "chassis");
                        var trackingUnitMake = ExtractUnitField(item, "tracking_unit_make", "tracking unit make", "device_make", "tracker_make") ?? unitType;
                        var imei = ExtractUnitField(item, "imei", "unit_imei", "device_imei", "sys_unique_id") ?? uniqueId;
                        var serialNumber = ExtractUnitField(item, "serial", "serial_number", "serial number", "serial#", "sn");
                        var iccid = ExtractUnitField(item, "iccid", "sim_iccid", "sim iccid");
                        var notes = ExtractUnitField(item, "notes", "note", "comment", "comments");

                        reports.Add(new WialonReport
                        {
                            Id = id,
                            Name = name ?? "Unknown Vehicle",
                            Client = client,
                            UnitType = unitType,
                            UniqueId = uniqueId,
                            Code = code,
                            Make = make,
                            Model = model,
                            Registration = registration,
                            FleetNumber = fleetNumber,
                            Colour = colour,
                            VinNumber = vin,
                            TrackingUnitMake = trackingUnitMake,
                            Imei = imei,
                            SerialNumber = serialNumber,
                            Iccid = iccid,
                            Notes = notes,
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

    public async Task<bool> IsImeiLoadedAsync(string? imei)
    {
        var normalizedImei = NormalizeImei(imei);
        if (string.IsNullOrWhiteSpace(normalizedImei))
            return false;

        if (string.IsNullOrEmpty(_sessionId))
            throw new Exception("Not connected to Wialon. Please connect first.");

        var attempts = new List<(string PropName, string PropValueMask)>
        {
            ("sys_unique_id", normalizedImei),
            ("sys_unique_id", $"*{normalizedImei}*"),
            ("unit_imei", normalizedImei),
            ("unit_imei", $"*{normalizedImei}*"),
            ("imei", normalizedImei),
            ("imei", $"*{normalizedImei}*")
        };

        foreach (var attempt in attempts)
        {
            var (reports, totalCount) = await GetReportsAsync(0, 100, attempt.PropName, attempt.PropValueMask);
            if (reports.Any(r => IsImeiMatch(r.UniqueId, normalizedImei)))
                return true;

            if (totalCount > 0 && reports.Count > 0)
                return true;
        }

        // Fallback: scan unit unique IDs in batches when indexed property lookups return no match.
        var (firstBatch, totalUnits) = await GetReportsAsync(0, 100, "sys_name", "*");
        if (firstBatch.Any(r => IsImeiMatch(r.UniqueId, normalizedImei)))
            return true;

        var scanLimit = Math.Min(totalUnits, 2000);
        for (var from = 100; from < scanLimit; from += 100)
        {
            var (batch, _) = await GetReportsAsync(from, 100, "sys_name", "*");
            if (batch.Any(r => IsImeiMatch(r.UniqueId, normalizedImei)))
                return true;
        }

        return false;
    }

    private static string? NormalizeImei(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        return digits;
    }

    private static bool IsImeiMatch(string? candidate, string normalizedImei)
    {
        var normalizedCandidate = NormalizeImei(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
            return false;

        return string.Equals(normalizedCandidate, normalizedImei, StringComparison.Ordinal)
            || normalizedCandidate.EndsWith(normalizedImei, StringComparison.Ordinal)
            || normalizedImei.EndsWith(normalizedCandidate, StringComparison.Ordinal);
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
            var latText = lat.ToString(CultureInfo.InvariantCulture);
            var lonText = lon.ToString(CultureInfo.InvariantCulture);

            // Use OpenStreetMap Nominatim for reverse geocoding (free, no API key required)
            var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latText}&lon={lonText}&zoom=18&addressdetails=1&accept-language=en";

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
