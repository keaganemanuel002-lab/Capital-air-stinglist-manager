using Avalonia.Controls;
using System;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class PurchaseOrdersWindow : Window
{
    public PurchaseOrdersWindow() : this(Environment.UserName, "Tech")
    {
    }

    public PurchaseOrdersWindow(string signedInUser, string signedInRole)
    {
        InitializeComponent();
        DataContext = new PurchaseOrdersViewModel(this, signedInUser, signedInRole);
    }
}
