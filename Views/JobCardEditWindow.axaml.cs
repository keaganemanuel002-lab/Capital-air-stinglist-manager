using Avalonia.Controls;
using Avalonia.Input;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class JobCardEditWindow : Window
{
    public JobCardEditWindow()
    {
        InitializeComponent();
        this.Loaded += JobCardEditWindow_Loaded;
    }

    private void JobCardEditWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is JobCardEditViewModel vm)
        {
            // Wire up Make TextBox text change
            var makeBox = this.FindControl<TextBox>("MakeTextBox");
            if (makeBox != null)
            {
                makeBox.TextChanged += (s, args) =>
                {
                    vm.FilterMakes(makeBox.Text);
                };
            }

            // Wire up Model TextBox text change
            var modelBox = this.FindControl<TextBox>("ModelTextBox");
            if (modelBox != null)
            {
                modelBox.TextChanged += (s, args) =>
                {
                    vm.FilterModels(modelBox.Text);
                };
            }

            // Wire up Make ListBox selection
            var makesListBox = this.FindControl<ListBox>("MakesListBox");
            if (makesListBox != null)
            {
                makesListBox.SelectionChanged += (s, args) =>
                {
                    if (makesListBox.SelectedItem is string selectedMake)
                    {
                        vm.SelectMake(selectedMake);
                        makesListBox.SelectedItem = null;
                    }
                };

                makesListBox.LostFocus += (s, args) =>
                {
                    vm.ShowMakesList = false;
                };
            }

            // Wire up Model ListBox selection
            var modelsListBox = this.FindControl<ListBox>("ModelsListBox");
            if (modelsListBox != null)
            {
                modelsListBox.SelectionChanged += (s, args) =>
                {
                    if (modelsListBox.SelectedItem is string selectedModel)
                    {
                        vm.SelectModel(selectedModel);
                        modelsListBox.SelectedItem = null;
                    }
                };

                modelsListBox.LostFocus += (s, args) =>
                {
                    vm.ShowModelsList = false;
                };
            }
        }
    }
}
