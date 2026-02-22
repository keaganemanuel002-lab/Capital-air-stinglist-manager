using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace StingListManager.Views;

public sealed class StartupSplashWindow : Window
{
    private readonly TextBlock _statusText;

    public StartupSplashWindow()
    {
        Title = "Capital Air (Pty) Ltd";
        Width = 520;
        Height = 220;
        MinWidth = 520;
        MinHeight = 220;
        MaxWidth = 520;
        MaxHeight = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _statusText = new TextBlock
        {
            Text = "Starting application...",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85
        };

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 8,
            MinWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Content = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(16),
            Classes = { "card" },
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Capital Air (Pty) Ltd",
                        FontSize = 24,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Loading, please wait...",
                        Classes = { "muted" }
                    },
                    progress,
                    _statusText
                }
            }
        };
    }

    public void SetStatus(string message)
    {
        _statusText.Text = string.IsNullOrWhiteSpace(message)
            ? "Loading..."
            : message.Trim();
    }
}

