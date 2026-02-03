using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.ViewModels;

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
}
