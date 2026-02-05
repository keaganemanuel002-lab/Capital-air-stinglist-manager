using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class QuotesView : UserControl
{
    public QuotesView() => InitializeComponent();

    private void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuotesViewModel viewModel && viewModel.ViewDetailsCommand.CanExecute(null))
        {
            viewModel.ViewDetailsCommand.Execute(null);
        }
    }
}
