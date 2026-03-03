using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StingListManager.Services;

public class PackagePricing
{
    public decimal StingBaseExVat { get; set; } = 500m;
    public decimal StingPlusBaseExVat { get; set; } = 700m;
    public decimal StingFmBaseExVat { get; set; } = 900m;
    public decimal PanicButtonAddonExVat { get; set; } = 100m;
    public decimal AppLiveTrackingAddonExVat { get; set; } = 100m;
}

public class AppSettings
{
    public string OperatorName { get; set; } = "";
    public string Role { get; set; } = "Admin"; // Admin | Ops | Tech | ReadOnly
    public decimal DefaultInstallFeeExVat { get; set; } = 150m;
    public decimal DefaultRemovalFeeExVat { get; set; } = 0m;
    public decimal DefaultInspectionFeeExVat { get; set; } = 450m;
    public decimal VatRate { get; set; } = 0.15m; // 15% VAT
    public PackagePricing PackagePricing { get; set; } = new();
    public string? SharedBaseDir { get; set; } = null;
    public bool UseSharedData { get; set; } = false;
    public bool AutoBackupOnStartup { get; set; } = true;
    public DateTime? LastAutoBackupDate { get; set; } = null;
    public string? TeltonikaApiKey { get; set; } = null;
    public string? FlickswitchBaseUrl { get; set; } = "https://app.simcontrol.co.za";
    public string? FlickswitchApiKey { get; set; } = null;
    public string? WialonApiToken { get; set; } = null;
    public string? WialonClientProvisionApiToken { get; set; } = null;
    public DateTime? LastWialonClientsSyncUtc { get; set; } = null;
    public List<string> LastWialonClientNames { get; set; } = new();
    public bool TechnicianApiEnabled { get; set; } = true;
    public int TechnicianApiPort { get; set; } = 5075;
    public string? TechnicianApiKey { get; set; } = null;
    public string? TechnicianLoginPin { get; set; } = "1234";
    public bool FirebaseSyncEnabled { get; set; } = false;
    public bool FirestorePrimaryDataEnabled { get; set; } = false;
    public bool MongoPrimaryDataEnabled { get; set; } = false;
    public string? FirebaseProjectId { get; set; } = null;
    public string? FirebaseStorageBucket { get; set; } = null;
    public string? FirebaseServiceAccountJsonPath { get; set; } = null;
    public int FirebaseSyncIntervalSeconds { get; set; } = 2;
    public string? MongoConnectionString { get; set; } = null;
    public string? MongoDatabaseName { get; set; } = "stinglistmanager";
    public bool RememberMe { get; set; } = false;
    public string? RememberedPasswordProtected { get; set; } = null;
    public List<FilterPreset> StingPresets { get; set; } = new();
}

public class SettingsService
{
    public AppSettings Load()
    {
        Paths.EnsureLocal();
        if (!File.Exists(Paths.SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(Paths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Paths.EnsureLocal();
            Normalize(settings);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Paths.SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    private static void Normalize(AppSettings settings)
    {
        // Desktop app runs in local-only mode.
        settings.FirebaseSyncEnabled = false;
        settings.FirestorePrimaryDataEnabled = false;
        settings.MongoPrimaryDataEnabled = false;

        settings.FirebaseProjectId = NormalizeLower(settings.FirebaseProjectId);
        settings.FirebaseStorageBucket = NormalizeLower(settings.FirebaseStorageBucket);
        settings.FirebaseServiceAccountJsonPath = NormalizeTrim(settings.FirebaseServiceAccountJsonPath);
        settings.MongoConnectionString = NormalizeTrim(settings.MongoConnectionString);
        settings.MongoDatabaseName = NormalizeTrim(settings.MongoDatabaseName);
        settings.RememberedPasswordProtected = NormalizeTrim(settings.RememberedPasswordProtected);
        settings.WialonApiToken = NormalizeTrim(settings.WialonApiToken);
        settings.WialonClientProvisionApiToken = NormalizeTrim(settings.WialonClientProvisionApiToken);
        settings.FlickswitchApiKey = NormalizeTrim(settings.FlickswitchApiKey);
        settings.FlickswitchBaseUrl = NormalizeTrim(settings.FlickswitchBaseUrl);

        if (!settings.RememberMe)
            settings.RememberedPasswordProtected = null;
    }

    private static string? NormalizeLower(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeTrim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
