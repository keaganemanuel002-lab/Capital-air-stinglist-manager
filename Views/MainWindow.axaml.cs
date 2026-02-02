using Avalonia.Controls;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(this);
    }
}