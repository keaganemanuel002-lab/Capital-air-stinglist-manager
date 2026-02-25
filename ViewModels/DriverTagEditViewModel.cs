using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class DriverTagEditViewModel : ViewModelBase
{
    private readonly int? _selectedId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    [ObservableProperty] private string windowTitle = "Issue Driver Tag";
    [ObservableProperty] private string saveButtonText = "Issue Tag";
    [ObservableProperty] private string tagCode = string.Empty;
    [ObservableProperty] private string driverName = string.Empty;
    [ObservableProperty] private DateTimeOffset? issuedAt = DateTimeOffset.Now.Date;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? errorMessage;

    public DriverTagEditViewModel(Action close, Action onSaved, AppState appState)
    {
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        _selectedId = null;
    }

    public DriverTagEditViewModel(int selectedId, Action close, Action onSaved, AppState appState)
    {
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        _selectedId = selectedId;

        WindowTitle = "Amend Driver Tag";
        SaveButtonText = "Save Changes";
        Load(selectedId);
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

        var normalizedTagCode = NormalizeSingleLine(TagCode).ToUpperInvariant();
        var normalizedDriver = NormalizeSingleLine(DriverName);
        if (string.IsNullOrWhiteSpace(normalizedTagCode))
        {
            ErrorMessage = "Tag code is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedDriver))
        {
            ErrorMessage = "Driver name is required.";
            return;
        }

        if (IssuedAt is null)
        {
            ErrorMessage = "Issued date is required.";
            return;
        }

        var codeNorm = NormalizeComparable(normalizedTagCode);

        using var db = new AppDbContext();
        var duplicate = db.DriverTags
            .AsNoTracking()
            .FirstOrDefault(x => x.TagCodeNorm == codeNorm && x.Id != (_selectedId ?? 0));
        if (duplicate is not null)
        {
            ErrorMessage = $"Tag code '{normalizedTagCode}' already exists.";
            return;
        }

        DriverTag record;
        if (_selectedId is int id)
        {
            record = db.DriverTags.FirstOrDefault(x => x.Id == id) ?? new DriverTag();
            if (record.Id == 0)
            {
                ErrorMessage = "Selected driver tag record no longer exists.";
                return;
            }
        }
        else
        {
            record = new DriverTag();
            db.DriverTags.Add(record);
        }

        record.TagCode = normalizedTagCode;
        record.DriverName = normalizedDriver;
        record.IssuedAt = IssuedAt.Value.UtcDateTime;
        record.Notes = TrimOrNull(Notes);

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            ErrorMessage = $"Tag code '{normalizedTagCode}' already exists.";
            return;
        }

        _appState.SetStatus(_selectedId is int
            ? $"Driver tag updated: {record.TagCode} -> {record.DriverName}."
            : $"Driver tag issued: {record.TagCode} to {record.DriverName}.");
        _onSaved();
        _close();
    }

    private void Load(int selectedId)
    {
        using var db = new AppDbContext();
        var record = db.DriverTags.AsNoTracking().FirstOrDefault(x => x.Id == selectedId);
        if (record is null)
        {
            ErrorMessage = "Driver tag record not found.";
            return;
        }

        TagCode = record.TagCode;
        DriverName = record.DriverName;
        IssuedAt = new DateTimeOffset(ToLocal(record.IssuedAt));
        Notes = record.Notes;
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeComparable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static DateTime ToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }
}
