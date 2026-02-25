using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class DriverTagsView : UserControl
{
    public DriverTagsView()
    {
        InitializeComponent();
    }

    private async void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DriverTagsViewModel viewModel)
            return;

        if (!viewModel.AmendSelectedCommand.CanExecute(null))
            return;

        await viewModel.AmendSelectedCommand.ExecuteAsync(null);
    }
}
