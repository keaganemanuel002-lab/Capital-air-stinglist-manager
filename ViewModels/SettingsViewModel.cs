using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

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
