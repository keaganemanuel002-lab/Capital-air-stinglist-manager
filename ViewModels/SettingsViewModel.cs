using System.Collections.Generic;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;
using StingListManager.Views;

namespace StingListManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly HttpClient ConnectivityHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private readonly Window _window;
    private readonly AppState _appState;
    private bool _showTechnicianApiKey;
    private bool _showTechnicianLoginPin;
    private string _connectivityStatusText = "Checking...";
    private string _connectivityDetailsText = "Connectivity check has not run yet.";
    private string _connectivityLastCheckedText = "Last checked: -";
    private IBrush _connectivityStatusBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private int _connectivityCheckRunning;

    public List<string> RoleOptions { get; } = new() { "Admin", "Ops", "Tech", "ReadOnly" };

    public SettingsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
    }

    public string OperatorName
    {
        get => _appState.OperatorName;
        set
        {
            _appState.OperatorName = value;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string Role
    {
        get => _appState.Role;
        set
        {
            _appState.Role = value;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public decimal DefaultInstallFeeExVat
    {
        get => _appState.Settings.DefaultInstallFeeExVat;
        set { _appState.Settings.DefaultInstallFeeExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal DefaultRemovalFeeExVat
    {
        get => _appState.Settings.DefaultRemovalFeeExVat;
        set { _appState.Settings.DefaultRemovalFeeExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal DefaultInspectionFeeExVat
    {
        get => _appState.Settings.DefaultInspectionFeeExVat;
        set { _appState.Settings.DefaultInspectionFeeExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal VatRate
    {
        get => _appState.Settings.VatRate * 100; // Display as percentage
        set { _appState.Settings.VatRate = value / 100; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal StingBaseExVat
    {
        get => _appState.Settings.PackagePricing.StingBaseExVat;
        set { _appState.Settings.PackagePricing.StingBaseExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal StingPlusBaseExVat
    {
        get => _appState.Settings.PackagePricing.StingPlusBaseExVat;
        set { _appState.Settings.PackagePricing.StingPlusBaseExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal StingFmBaseExVat
    {
        get => _appState.Settings.PackagePricing.StingFmBaseExVat;
        set { _appState.Settings.PackagePricing.StingFmBaseExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal PanicButtonAddonExVat
    {
        get => _appState.Settings.PackagePricing.PanicButtonAddonExVat;
        set { _appState.Settings.PackagePricing.PanicButtonAddonExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public decimal AppLiveTrackingAddonExVat
    {
        get => _appState.Settings.PackagePricing.AppLiveTrackingAddonExVat;
        set { _appState.Settings.PackagePricing.AppLiveTrackingAddonExVat = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public bool UseSharedData
    {
        get => _appState.Settings.UseSharedData;
        set { _appState.Settings.UseSharedData = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public string SharedBaseDir
    {
        get => _appState.Settings.SharedBaseDir ?? string.Empty;
        set { _appState.Settings.SharedBaseDir = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public bool AutoBackupOnStartup
    {
        get => _appState.Settings.AutoBackupOnStartup;
        set { _appState.Settings.AutoBackupOnStartup = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public string TeltonikaApiKey
    {
        get => _appState.Settings.TeltonikaApiKey ?? string.Empty;
        set { _appState.Settings.TeltonikaApiKey = value; _appState.SaveSettings(); OnPropertyChanged(); }
    }

    public string FlickswitchBaseUrl
    {
        get => _appState.Settings.FlickswitchBaseUrl ?? "https://app.simcontrol.co.za";
        set
        {
            _appState.Settings.FlickswitchBaseUrl = string.IsNullOrWhiteSpace(value)
                ? "https://app.simcontrol.co.za"
                : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string FlickswitchApiKey
    {
        get
        {
            var value = _appState.Settings.FlickswitchApiKey ?? string.Empty;
            return value.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
        }
        set
        {
            _appState.Settings.FlickswitchApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string WialonApiToken
    {
        get => _appState.Settings.WialonApiToken ?? string.Empty;
        set
        {
            _appState.Settings.WialonApiToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string WialonClientProvisionApiToken
    {
        get => _appState.Settings.WialonClientProvisionApiToken ?? string.Empty;
        set
        {
            _appState.Settings.WialonClientProvisionApiToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool TechnicianApiEnabled
    {
        get => _appState.Settings.TechnicianApiEnabled;
        set
        {
            _appState.Settings.TechnicianApiEnabled = value;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public int TechnicianApiPort
    {
        get => _appState.Settings.TechnicianApiPort <= 0 ? 5075 : _appState.Settings.TechnicianApiPort;
        set
        {
            var port = value is < 1024 or > 65535 ? 5075 : value;
            _appState.Settings.TechnicianApiPort = port;
            _appState.SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TechnicianPortalUrls));
        }
    }

    public string TechnicianApiKey
    {
        get => _appState.Settings.TechnicianApiKey ?? string.Empty;
        set
        {
            _appState.Settings.TechnicianApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string TechnicianLoginPin
    {
        get => _appState.Settings.TechnicianLoginPin ?? "1234";
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "1234" : value.Trim();
            _appState.Settings.TechnicianLoginPin = normalized;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool FirebaseSyncEnabled
    {
        get => _appState.Settings.FirebaseSyncEnabled;
        set
        {
            _appState.Settings.FirebaseSyncEnabled = value;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool FirestorePrimaryDataEnabled
    {
        get => _appState.Settings.FirestorePrimaryDataEnabled;
        set
        {
            _appState.Settings.FirestorePrimaryDataEnabled = value;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string FirebaseProjectId
    {
        get => _appState.Settings.FirebaseProjectId ?? string.Empty;
        set
        {
            _appState.Settings.FirebaseProjectId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string FirebaseStorageBucket
    {
        get => _appState.Settings.FirebaseStorageBucket ?? string.Empty;
        set
        {
            _appState.Settings.FirebaseStorageBucket = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string FirebaseServiceAccountJsonPath
    {
        get => _appState.Settings.FirebaseServiceAccountJsonPath ?? string.Empty;
        set
        {
            _appState.Settings.FirebaseServiceAccountJsonPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public int FirebaseSyncIntervalSeconds
    {
        get => _appState.Settings.FirebaseSyncIntervalSeconds <= 0 ? 5 : _appState.Settings.FirebaseSyncIntervalSeconds;
        set
        {
            var seconds = value < 2 ? 2 : value;
            _appState.Settings.FirebaseSyncIntervalSeconds = seconds;
            _appState.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool ShowTechnicianApiKey
    {
        get => _showTechnicianApiKey;
        set
        {
            if (_showTechnicianApiKey == value)
                return;

            _showTechnicianApiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TechnicianApiKeyToggleText));
        }
    }

    public string TechnicianApiKeyToggleText => ShowTechnicianApiKey ? "Hide" : "Reveal";
    public bool ShowTechnicianLoginPin
    {
        get => _showTechnicianLoginPin;
        set
        {
            if (_showTechnicianLoginPin == value)
                return;

            _showTechnicianLoginPin = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TechnicianLoginPinToggleText));
        }
    }

    public string TechnicianLoginPinToggleText => ShowTechnicianLoginPin ? "Hide" : "Reveal";

    public string TechnicianPortalUrls => string.Join(Environment.NewLine, TechnicianApiHostService.GetSuggestedPortalUrls(TechnicianApiPort));

    public string ConnectivityStatusText
    {
        get => _connectivityStatusText;
        private set
        {
            _connectivityStatusText = value;
            OnPropertyChanged();
        }
    }

    public string ConnectivityDetailsText
    {
        get => _connectivityDetailsText;
        private set
        {
            _connectivityDetailsText = value;
            OnPropertyChanged();
        }
    }

    public string ConnectivityLastCheckedText
    {
        get => _connectivityLastCheckedText;
        private set
        {
            _connectivityLastCheckedText = value;
            OnPropertyChanged();
        }
    }

    public IBrush ConnectivityStatusBrush
    {
        get => _connectivityStatusBrush;
        private set
        {
            _connectivityStatusBrush = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void GenerateTechnicianApiKey()
    {
        _appState.Settings.TechnicianApiKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        _appState.SaveSettings();
        OnPropertyChanged(nameof(TechnicianApiKey));
    }

    [RelayCommand]
    private void ToggleTechnicianApiKeyVisibility()
    {
        ShowTechnicianApiKey = !ShowTechnicianApiKey;
    }

    [RelayCommand]
    private void ToggleTechnicianLoginPinVisibility()
    {
        ShowTechnicianLoginPin = !ShowTechnicianLoginPin;
    }

    [RelayCommand]
    private async Task BrowseFirebaseServiceAccountPath()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Firebase service account JSON",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON files")
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        var file = files?.FirstOrDefault();
        if (file is null) return;
        FirebaseServiceAccountJsonPath = file.Path.LocalPath;
    }

    [RelayCommand]
    private async Task RefreshConnectivityStatus()
    {
        if (Interlocked.Exchange(ref _connectivityCheckRunning, 1) == 1)
            return;

        try
        {
            var now = DateTime.Now;
            ConnectivityLastCheckedText = $"Last checked: {now:yyyy-MM-dd HH:mm:ss}";

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                ConnectivityStatusText = "Offline";
                ConnectivityDetailsText = "No active network interface detected.";
                ConnectivityStatusBrush = new SolidColorBrush(Color.Parse("#DC2626"));
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://clients3.google.com/generate_204");
                using var response = await ConnectivityHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    ConnectivityStatusText = "Online";
                    ConnectivityDetailsText = "Internet reachability check succeeded.";
                    ConnectivityStatusBrush = new SolidColorBrush(Color.Parse("#16A34A"));
                }
                else
                {
                    ConnectivityStatusText = "Offline";
                    ConnectivityDetailsText = $"Reachability check failed (HTTP {(int)response.StatusCode}).";
                    ConnectivityStatusBrush = new SolidColorBrush(Color.Parse("#DC2626"));
                }
            }
            catch (Exception ex)
            {
                ConnectivityStatusText = "Offline";
                ConnectivityDetailsText = $"Reachability check failed: {ex.Message}";
                ConnectivityStatusBrush = new SolidColorBrush(Color.Parse("#DC2626"));
            }
        }
        finally
        {
            Interlocked.Exchange(ref _connectivityCheckRunning, 0);
        }
    }

    [RelayCommand]
    private async Task OpenConnectivitySettings()
    {
        var dialog = new ConnectivitySettingsWindow
        {
            DataContext = this
        };

        await dialog.ShowDialog(_window);
    }

    [RelayCommand]
    private void Save()
    {
        _appState.SaveSettings();
        _appState.SetStatus("Settings saved. Restart required for data location changes.");
    }

    [RelayCommand]
    private async Task BrowseSharedBaseDir()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select shared data folder",
            AllowMultiple = false
        });

        var folder = folders?.FirstOrDefault();
        if (folder is null) return;

        SharedBaseDir = folder.Path.LocalPath;
    }
}
