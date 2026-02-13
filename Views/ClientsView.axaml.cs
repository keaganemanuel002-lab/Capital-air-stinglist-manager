using Avalonia.Controls;
using Avalonia.Interactivity;
using StingListManager.Data.Entities;
using StingListManager.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace StingListManager.Views;

public partial class ClientsView : UserControl
{
    public ClientsView()
    {
        InitializeComponent();
    }

    private async void NewClient_Click(object? sender, RoutedEventArgs e)
    {
        await OpenClientEditorAsync(null);
    }

    private async void EditClient_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm) return;
        if (vm.SelectedRow is null)
        {
            vm.SetStatus("Select a client to edit.");
            return;
        }

        await OpenClientEditorAsync(vm.SelectedRow);
    }

    private async void Grid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClientsViewModel vm) return;
        if (vm.SelectedRow is null) return;

        await OpenClientEditorAsync(vm.SelectedRow);
    }

    private async Task OpenClientEditorAsync(Client? existing)
    {
        if (DataContext is not ClientsViewModel vm) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var window = new ClientEditWindow();
        window.DataContext = new ClientEditViewModel(
            existing,
            () => window.Close(),
            savedId =>
            {
                if (vm.LoadCommand.CanExecute(null))
                {
                    vm.LoadCommand.Execute(null);
                }

                vm.SelectedRow = vm.Rows.FirstOrDefault(c => c.Id == savedId);
            },
            vm.SetStatus);

        await window.ShowDialog(owner);
    }
}
