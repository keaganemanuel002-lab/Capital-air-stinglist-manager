using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StingListManager.Views;

public partial class DashcamEditorWindow : Window
{
    public DashcamEditorWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
