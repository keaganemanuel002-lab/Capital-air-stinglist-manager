using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using StingListManager.ViewModels;
using System.Collections;
using System.Linq;

namespace StingListManager.Views;

public partial class JobCardsView : UserControl
{
    public JobCardsView() => InitializeComponent();

    private void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is JobCardsViewModel viewModel && viewModel.EditSelectedCommand.CanExecute(null))
        {
            viewModel.EditSelectedCommand.Execute(null);
        }
    }

    private void Grid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is JobCardsViewModel viewModel)
        {
            viewModel.SelectedRows = grid.SelectedItems?.Cast<JobCardRow>().ToList();
        }
    }
}
