using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StingListManager.Services;

public partial class AppState : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    public AppSettings Settings { get; private set; }

    public AppState()
    {
        Settings = _settingsService.Load();

        if (string.IsNullOrWhiteSpace(Settings.OperatorName))
            Settings.OperatorName = Environment.UserName;

        // Initialize default filter presets if empty
        if (Settings.StingPresets.Count == 0)
        {
            Settings.StingPresets.Add(new FilterPreset { Name = "Active Only", ShowArchived = false });
            Settings.StingPresets.Add(new FilterPreset { Name = "All (Including Archived)", ShowArchived = true });
            SaveSettings();
        }

        OperatorName = Settings.OperatorName;
        Role = Settings.Role;
        SetStatus("Ready.");
    }

    [ObservableProperty] private string operatorName = "";
    [ObservableProperty] private string role = "Admin";
    [ObservableProperty] private string statusMessage = "Ready.";
    [ObservableProperty] private string statusTime = "";
    [ObservableProperty] private bool statusIsError;

    public void SetStatus(string message, bool isError = false)
    {
        StatusIsError = isError || LooksLikeError(message);
        StatusMessage = message;
        StatusTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    private static bool LooksLikeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var value = message.Trim();
        var errorMarkers = new[]
        {
            "error",
            "failed",
            "cannot",
            "can't",
            "not permitted",
            "not found",
            "missing",
            "invalid",
            "blocked",
            "exception"
        };

        foreach (var marker in errorMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void SaveSettings()
    {
        Settings.OperatorName = OperatorName;
        Settings.Role = Role;
        _settingsService.Save(Settings);
        
        // Notify UI of permission changes
        OnPropertyChanged(nameof(CanApproveQuotes));
        OnPropertyChanged(nameof(CanCompleteJobs));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanArchive));
    }

    public bool CanApproveQuotes => Role is "Admin" or "Ops";
    public bool CanCompleteJobs => Role is "Admin" or "Ops" or "Tech";
    public bool CanExport => Role is "Admin" or "Ops";
    public bool CanArchive => Role is "Admin" or "Ops";
    public bool IsAdmin => Role == "Admin";
}
