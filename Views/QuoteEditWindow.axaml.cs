using System;
using Avalonia.Controls;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class QuoteEditWindow : Window
{
    public QuoteEditWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not QuoteEditViewModel vm)
            return;

    }
}
