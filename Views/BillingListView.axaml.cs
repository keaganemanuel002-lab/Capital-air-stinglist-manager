using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class BillingListView : UserControl
{
    public BillingListView()
    {
        InitializeComponent();
    }

    private void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BillingListViewModel viewModel && viewModel.ViewDetailsCommand.CanExecute(null))
        {
            viewModel.ViewDetailsCommand.Execute(null);
        }
    }

    private void Grid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is null)
            return;

        grid.ScrollIntoView(grid.SelectedItem, null);
    }
}
