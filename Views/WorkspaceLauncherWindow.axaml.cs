using Avalonia.Controls;
using System;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public enum WorkspaceChoice
{
    StingManager = 1,
    Orders = 2
}

public partial class WorkspaceLauncherWindow : Window
{
    public WorkspaceChoice? SelectedWorkspace { get; private set; }
    public string AuthenticatedUsername { get; }
    public string AuthenticatedRole { get; }

    public WorkspaceLauncherWindow() : this(Environment.UserName, "Tech")
    {
    }

    public WorkspaceLauncherWindow(string username, string role)
    {
        AuthenticatedUsername = username;
        AuthenticatedRole = role;

        InitializeComponent();
        DataContext = new WorkspaceLauncherViewModel(this, username, role);
    }

    public void ChooseOrders()
    {
        SelectedWorkspace = WorkspaceChoice.Orders;
        Close();
    }

    public void ChooseStingManager()
    {
        SelectedWorkspace = WorkspaceChoice.StingManager;
        Close();
    }
}
