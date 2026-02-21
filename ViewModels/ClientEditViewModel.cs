using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.ViewModels;

public partial class ClientEditViewModel : ViewModelBase
{
    private readonly int? _clientId;
    private readonly Action _close;
    private readonly Action<int> _onSaved;
    private readonly Action<string> _setStatus;

    [ObservableProperty] private string windowTitle = "New Client";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string? contactPerson;
    [ObservableProperty] private string? phoneNumber;
    [ObservableProperty] private string? emailAddress;
    [ObservableProperty] private string? address;
    [ObservableProperty] private string? errorMessage;

    public ClientEditViewModel(
        Client? existing,
        Action close,
        Action<int> onSaved,
        Action<string> setStatus)
    {
        _clientId = existing?.Id;
        _close = close;
        _onSaved = onSaved;
        _setStatus = setStatus;

        if (existing is not null)
        {
            WindowTitle = "Edit Client";
            Name = existing.Name;
            ContactPerson = existing.ContactPerson;
            PhoneNumber = existing.PhoneNumber;
            EmailAddress = existing.EmailAddress;
            Address = existing.Address;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _close();
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        var normalizedName = Name.Trim();
        var normalizedComparableName = NormalizeComparableText(normalizedName);

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            ErrorMessage = "Client name is required.";
            return;
        }

        using var db = new AppDbContext();

        var duplicate = db.Clients
            .AsNoTracking()
            .FirstOrDefault(c => c.Id != (_clientId ?? 0) &&
                                 c.NameNorm == normalizedComparableName);

        if (duplicate is not null)
        {
            ErrorMessage = "Client name already exists.";
            return;
        }

        Client entity;
        if (_clientId.HasValue)
        {
            var existing = db.Clients.FirstOrDefault(c => c.Id == _clientId.Value);
            if (existing is null)
            {
                ErrorMessage = "The selected client no longer exists.";
                return;
            }

            entity = existing;
        }
        else
        {
            entity = new Client { CreatedAt = DateTime.UtcNow };
            db.Clients.Add(entity);
        }

        entity.Name = normalizedName;
        entity.ContactPerson = ContactPerson?.Trim();
        entity.PhoneNumber = PhoneNumber?.Trim();
        entity.EmailAddress = EmailAddress?.Trim();
        entity.Address = Address?.Trim();

        db.SaveChanges();
        _setStatus("Client saved locally. Use 'Sync from Wialon' to refresh accounts.");

        _onSaved(entity.Id);
        _close();
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
