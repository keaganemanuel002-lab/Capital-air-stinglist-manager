using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;

namespace StingListManager.Services;

public static class DialogService
{
    public static Task Alert(string title, string message)
    {
        var owner = GetMainWindow();
        return Alert(owner, title, message);
    }

    public static async Task Alert(Window? owner, string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            CanResize = false
        };

        var okBtn = new Button { Content = "OK" };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { okBtn }
        };

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons
            }
        };

        dlg.Content = content;

        okBtn.Click += (_, __) => dlg.Close();

        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var ok = false;

        var cancelBtn = new Button { Content = "Cancel" };
        var confirmBtn = new Button { Content = "Confirm" };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelBtn, confirmBtn }
        };

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons
            }
        };

        dlg.Content = content;

        cancelBtn.Click += (_, __) => { ok = false; dlg.Close(); };
        confirmBtn.Click += (_, __) => { ok = true; dlg.Close(); };

        await dlg.ShowDialog(owner);
        return ok;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
