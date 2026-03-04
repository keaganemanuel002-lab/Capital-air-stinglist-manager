using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;
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
        if (DataContext is not BillingListViewModel viewModel)
            return;

        if (sender is DataGrid grid && grid.SelectedItem is BillingListRow row && row.IsClientSummaryRow)
        {
            if (viewModel.ToggleClientLiveTrackingCommand.CanExecute(row))
            {
                viewModel.ToggleClientLiveTrackingCommand.Execute(row);
            }

            return;
        }

        if (viewModel.ViewDetailsCommand.CanExecute(null))
        {
            viewModel.ViewDetailsCommand.Execute(null);
        }
    }

    private void Grid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not BillingListViewModel viewModel)
            return;

        viewModel.SelectedRows = grid.SelectedItems?.Cast<BillingListRow>().ToList();

        if (grid.SelectedItem is not null)
            grid.ScrollIntoView(grid.SelectedItem, null);
    }
}
