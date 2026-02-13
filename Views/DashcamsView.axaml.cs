using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StingListManager.ViewModels;
using System.Threading.Tasks;

namespace StingListManager.Views;

public partial class DashcamsView : UserControl
{
    public DashcamsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        await OpenEditorAsync();
    }

    private async void EditSelected_Click(object? sender, RoutedEventArgs e)
    {
        await OpenEditorAsync();
    }

    private async void NewDashcam_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashcamsViewModel viewModel) return;
        if (!viewModel.AddNewCommand.CanExecute(null)) return;

        viewModel.AddNewCommand.Execute(null);
        await OpenEditorAsync();
    }

    private async Task OpenEditorAsync()
    {
        if (DataContext is not DashcamsViewModel viewModel) return;
        if (viewModel.Selected is null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var editor = new DashcamEditorWindow
        {
            DataContext = viewModel
        };

        await editor.ShowDialog(owner);
    }
}
