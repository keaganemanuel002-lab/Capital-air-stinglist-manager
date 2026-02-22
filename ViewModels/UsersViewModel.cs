using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class UsersViewModel : ViewModelBase
{
    private readonly AppState _appState;
    private readonly AuthService _authService = new();

    public sealed partial class UserRow : ObservableObject
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Role { get; init; } = "Tech";
        public bool IsActive { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string LastLoginAt { get; init; } = "-";
        public string StatusText => IsActive ? "Active" : "Disabled";
    }

    [ObservableProperty] private UserRow? selectedUser;
    [ObservableProperty] private string newUsername = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string newRole = "Tech";
    [ObservableProperty] private bool newIsActive = true;
    [ObservableProperty] private string resetPassword = string.Empty;
    [ObservableProperty] private string selectedUserRole = "Tech";

    public ObservableCollection<UserRow> Users { get; } = new();
    public IReadOnlyList<string> RoleOptions { get; } = new[] { "Admin", "Ops", "Tech", "ReadOnly" };
    public bool CanManageUsers => _appState.Role == "Admin";

    public UsersViewModel(AppState appState)
    {
        _appState = appState;
        Refresh();
    }

    partial void OnSelectedUserChanged(UserRow? value)
    {
        SelectedUserRole = value?.Role ?? "Tech";
    }

    [RelayCommand]
    private void Refresh()
    {
        Users.Clear();
        foreach (var row in _authService.GetUsers())
        {
            Users.Add(new UserRow
            {
                Id = row.Id,
                Username = row.Username,
                Role = row.Role,
                IsActive = row.IsActive,
                CreatedAt = row.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                LastLoginAt = row.LastLoginAt.HasValue
                    ? row.LastLoginAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : "-"
            });
        }
    }

    [RelayCommand]
    private void CreateUser()
    {
        if (!CanManageUsers)
        {
            _appState.SetStatus("Only Admin users can create users.", true);
            return;
        }

        var result = _authService.CreateUser(NewUsername, NewPassword, NewRole, NewIsActive);
        _appState.SetStatus(result.message, !result.ok);
        if (!result.ok)
            return;

        NewUsername = string.Empty;
        NewPassword = string.Empty;
        NewRole = "Tech";
        NewIsActive = true;
        Refresh();
    }

    [RelayCommand]
    private void ResetSelectedPassword()
    {
        if (!CanManageUsers)
        {
            _appState.SetStatus("Only Admin users can reset passwords.", true);
            return;
        }

        if (SelectedUser is null)
        {
            _appState.SetStatus("Select a user first.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(ResetPassword))
        {
            _appState.SetStatus("Enter a new password first.", true);
            return;
        }

        var selectedId = SelectedUser.Id;
        var result = _authService.ResetPassword(selectedId, ResetPassword);
        _appState.SetStatus(result.message, !result.ok);
        if (!result.ok)
            return;

        ResetPassword = string.Empty;
        Refresh();
        SelectedUser = Users.FirstOrDefault(u => u != null && u.Id == selectedId);
    }

    [RelayCommand]
    private void ToggleSelectedActive()
    {
        if (!CanManageUsers)
        {
            _appState.SetStatus("Only Admin users can change user status.", true);
            return;
        }

        if (SelectedUser is null)
        {
            _appState.SetStatus("Select a user first.", true);
            return;
        }

        var result = _authService.SetActive(SelectedUser.Id, !SelectedUser.IsActive);
        _appState.SetStatus(result.message, !result.ok);
        if (!result.ok)
            return;

        var selectedId = SelectedUser.Id;
        Refresh();
        SelectedUser = Users.FirstOrDefault(u => u != null && u.Id == selectedId);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (!CanManageUsers)
        {
            _appState.SetStatus("Only Admin users can delete users.", true);
            return;
        }

        if (SelectedUser is null)
        {
            _appState.SetStatus("Select a user first.", true);
            return;
        }

        var selectedId = SelectedUser.Id;
        var result = _authService.DeleteUser(selectedId);
        _appState.SetStatus(result.message, !result.ok);
        if (!result.ok)
            return;

        Refresh();
        SelectedUser = null;
    }

    [RelayCommand]
    private void ApplySelectedRole()
    {
        if (!CanManageUsers)
        {
            _appState.SetStatus("Only Admin users can change roles.", true);
            return;
        }

        if (SelectedUser is null)
        {
            _appState.SetStatus("Select a user first.", true);
            return;
        }

        var selectedId = SelectedUser.Id;
        var result = _authService.UpdateRole(selectedId, SelectedUserRole);
        _appState.SetStatus(result.message, !result.ok);
        if (!result.ok)
            return;

        Refresh();
        SelectedUser = Users.FirstOrDefault(u => u != null && u.Id == selectedId);
    }
}
