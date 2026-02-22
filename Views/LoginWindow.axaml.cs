using Avalonia.Controls;
using StingListManager.ViewModels;

namespace StingListManager.Views;

public partial class LoginWindow : Window
{
    public bool LoginSucceeded { get; private set; }
    public string AuthenticatedUsername { get; private set; } = string.Empty;
    public string AuthenticatedRole { get; private set; } = "Tech";

    public LoginWindow()
    {
        InitializeComponent();
        DataContext = new LoginViewModel(this);
    }

    public void CompleteLogin(string username, string role)
    {
        LoginSucceeded = true;
        AuthenticatedUsername = username;
        AuthenticatedRole = role;
        Close();
    }
}
