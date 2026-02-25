using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class PhoneIssueLogView : UserControl
{
    public PhoneIssueLogView()
    {
        InitializeComponent();
    }

    private async void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PhoneIssueLogViewModel viewModel)
            return;

        if (!viewModel.EditSelectedCommand.CanExecute(null))
            return;

        await viewModel.EditSelectedCommand.ExecuteAsync(null);
    }
}
