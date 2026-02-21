using Avalonia.Controls;
using Avalonia.Input;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class MainWindow : Window
{
    private const double CollapsedSidebarWidth = 74;
    private const double ExpandedSidebarWidth = 260;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(this);
        CollapseSidebar();
    }

    private void Sidebar_PointerEntered(object? sender, PointerEventArgs e)
    {
        ExpandSidebar();
    }

    private void Sidebar_PointerExited(object? sender, PointerEventArgs e)
    {
        CollapseSidebar();
    }

    private void ExpandSidebar()
    {
        SidebarNav.Width = ExpandedSidebarWidth;
        NavTitle.IsVisible = true;
        ExpandedNavList.IsVisible = true;
        CollapsedNavList.IsVisible = false;
    }

    private void CollapseSidebar()
    {
        SidebarNav.Width = CollapsedSidebarWidth;
        NavTitle.IsVisible = false;
        ExpandedNavList.IsVisible = false;
        CollapsedNavList.IsVisible = true;
    }
}
