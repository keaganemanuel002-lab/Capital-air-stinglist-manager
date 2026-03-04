using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class StingListView : UserControl
{
    public StingListView()
    {
        InitializeComponent();
    }

    private void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StingListViewModel viewModel && viewModel.ViewDetailsCommand.CanExecute(null))
        {
            viewModel.ViewDetailsCommand.Execute(null);
        }
    }

    private void Grid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is StingListViewModel viewModel)
        {
            viewModel.SelectedRows = grid.SelectedItems?.Cast<StingListRow>().ToList();
        }
    }
}
