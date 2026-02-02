using Avalonia.Controls;
using Avalonia.Input;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class SearchView : UserControl
{
    public SearchView() => InitializeComponent();

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchViewModel vm)
            vm.RunSearchCommand.Execute(null);
    }
}
