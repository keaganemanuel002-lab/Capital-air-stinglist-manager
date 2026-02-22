using Avalonia.Controls;
using Avalonia.Threading;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class ConnectivitySettingsWindow : Window
{
    private DispatcherTimer? _connectivityTimer;

    public ConnectivitySettingsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void CloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        vm.RefreshConnectivityStatusCommand.Execute(null);

        _connectivityTimer = new DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(10)
        };

        _connectivityTimer.Tick += (_, _) =>
        {
            vm.RefreshConnectivityStatusCommand.Execute(null);
        };
        _connectivityTimer.Start();
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        if (_connectivityTimer is null)
            return;

        _connectivityTimer.Stop();
        _connectivityTimer = null;
    }
}
