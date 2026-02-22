using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;
using StingListManager.Views;

namespace StingListManager.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly LoginWindow _window;
    private readonly AuthService _authService = new();

    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string message = "Sign in to continue.";
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool isBusy;
    public string MessageColor => IsError ? "#B91C1C" : "#334155";

    public LoginViewModel(LoginWindow window)
    {
        _window = window;
        Username = new SettingsService().Load().OperatorName ?? string.Empty;
    }

    partial void OnIsErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(MessageColor));
    }

    [RelayCommand]
    private void Login()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsError = false;
        Message = "Signing in...";

        var result = _authService.Login(Username, Password);
        if (!result.Ok || result.User is null)
        {
            IsBusy = false;
            IsError = true;
            Message = result.Message;
            return;
        }

        Message = result.Message;
        _window.CompleteLogin(result.User.Username, result.User.Role);
        IsBusy = false;
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Dispatcher.UIThread.Post(() => _window.Close());
    }
}
