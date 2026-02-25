using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Views;

namespace StingListManager.ViewModels;

public partial class WorkspaceLauncherViewModel : ViewModelBase
{
    private readonly WorkspaceLauncherWindow _window;

    public string SignedInAs { get; }

    public WorkspaceLauncherViewModel(WorkspaceLauncherWindow window, string signedInUser, string signedInRole)
    {
        _window = window;
        var user = string.IsNullOrWhiteSpace(signedInUser) ? "Unknown" : signedInUser.Trim();
        var role = string.IsNullOrWhiteSpace(signedInRole) ? "Tech" : signedInRole.Trim();
        SignedInAs = $"{user} ({role})";
    }

    [RelayCommand]
    private void OpenOrders()
    {
        _window.ChooseOrders();
    }

    [RelayCommand]
    private void OpenStingManager()
    {
        _window.ChooseStingManager();
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        _window.Close();
    }
}
