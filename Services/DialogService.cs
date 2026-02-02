using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StingListManager.Services;

public static class DialogService
{
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
}
