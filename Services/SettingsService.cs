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
    public decimal VatRate { get; set; } = 0.15m; // 15% VAT
    public PackagePricing PackagePricing { get; set; } = new();
    public string? SharedBaseDir { get; set; } = null;
    public bool UseSharedData { get; set; } = false;
    public bool AutoBackupOnStartup { get; set; } = true;
    public DateTime? LastAutoBackupDate { get; set; } = null;
    public string? TeltonikaApiKey { get; set; } = null;
    public string? WialonApiToken { get; set; } = null;
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
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Paths.SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }
}
