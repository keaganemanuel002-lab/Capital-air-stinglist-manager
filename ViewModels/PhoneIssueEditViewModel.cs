using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class PhoneIssueEditViewModel : ViewModelBase
{
    private readonly int? _entryId;
    private readonly Action _close;
    private readonly Action _onSaved;
    private readonly AppState _appState;

    [ObservableProperty] private string windowTitle = "Issue Phone";
    [ObservableProperty] private string saveButtonText = "Issue Phone";
    [ObservableProperty] private string teamName = string.Empty;
    [ObservableProperty] private string vehicleRegistration = string.Empty;
    [ObservableProperty] private string teamMemberOne = string.Empty;
    [ObservableProperty] private string teamMemberTwo = string.Empty;
    [ObservableProperty] private string? phoneLabel;
    [ObservableProperty] private string? phoneNumber;
    [ObservableProperty] private string? phoneImei;
    [ObservableProperty] private DateTimeOffset? issuedAt = DateTimeOffset.Now.Date;
    [ObservableProperty] private bool isReturned;
    [ObservableProperty] private DateTimeOffset? returnedAt;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string? selectedTeamSuggestion;
    [ObservableProperty] private string? selectedVehicleSuggestion;
    [ObservableProperty] private string? errorMessage;

    public ObservableCollection<string> TeamOptions { get; } = new();
    public ObservableCollection<string> VehicleOptions { get; } = new();
    public bool CanSetReturnedDate => IsReturned;

    public PhoneIssueEditViewModel(Action close, Action onSaved, AppState appState)
    {
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        _entryId = null;

        WindowTitle = "Issue Phone To Team";
        SaveButtonText = "Issue Phone";

        LoadLookupData();
    }

    public PhoneIssueEditViewModel(int entryId, Action close, Action onSaved, AppState appState)
    {
        _close = close;
        _onSaved = onSaved;
        _appState = appState;
        _entryId = entryId;

        WindowTitle = "Amend Phone Issue";
        SaveButtonText = "Save Changes";

        LoadLookupData();
        LoadExisting(entryId);
    }

    partial void OnIsReturnedChanged(bool value)
    {
        if (value && ReturnedAt is null)
            ReturnedAt = DateTimeOffset.Now.Date;
        if (!value)
            ReturnedAt = null;

        OnPropertyChanged(nameof(CanSetReturnedDate));
    }

    partial void OnSelectedTeamSuggestionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            TeamName = value;
    }

    partial void OnSelectedVehicleSuggestionChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            VehicleRegistration = value;
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

        var team = NormalizeSingleLine(TeamName);
        var vehicle = NormalizeSingleLine(VehicleRegistration).ToUpperInvariant();
        var memberOne = NormalizeSingleLine(TeamMemberOne);
        var memberTwo = NormalizeSingleLine(TeamMemberTwo);
        var issuedDate = IssuedAt;

        if (string.IsNullOrWhiteSpace(team))
        {
            ErrorMessage = "Team name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(vehicle))
        {
            ErrorMessage = "Vehicle registration is required.";
            return;
        }

        if (VehicleOptions.Count > 0
            && !VehicleOptions.Any(x => string.Equals(x, vehicle, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = "Vehicle must be selected from known vehicle registrations.";
            return;
        }

        if (string.IsNullOrWhiteSpace(memberOne) || string.IsNullOrWhiteSpace(memberTwo))
        {
            ErrorMessage = "Both team member names are required.";
            return;
        }

        if (string.Equals(memberOne, memberTwo, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Team members must be two different people.";
            return;
        }

        if (issuedDate is null)
        {
            ErrorMessage = "Issued date is required.";
            return;
        }

        if (IsReturned && ReturnedAt is not DateTimeOffset returnedDate)
        {
            ErrorMessage = "Returned date is required when marking this phone as returned.";
            return;
        }

        if (IsReturned && ReturnedAt!.Value.Date < issuedDate.Value.Date)
        {
            ErrorMessage = "Returned date cannot be earlier than issued date.";
            return;
        }

        using var db = new AppDbContext();
        PhoneIssueLogEntry entry;
        if (_entryId is int existingId)
        {
            var existing = db.PhoneIssueLogEntries.FirstOrDefault(x => x.Id == existingId);
            if (existing is null)
            {
                ErrorMessage = "Phone issue record no longer exists.";
                return;
            }

            entry = existing;
        }
        else
        {
            entry = new PhoneIssueLogEntry();
            db.PhoneIssueLogEntries.Add(entry);
        }

        entry.TeamName = team;
        entry.VehicleRegistration = vehicle;
        entry.TeamMemberOne = memberOne;
        entry.TeamMemberTwo = memberTwo;
        entry.PhoneLabel = TrimOrNull(PhoneLabel);
        entry.PhoneNumber = TrimOrNull(PhoneNumber);
        entry.PhoneImei = TrimOrNull(PhoneImei);
        entry.IssuedAt = issuedDate.Value.UtcDateTime;
        entry.ReturnedAt = IsReturned ? ReturnedAt?.UtcDateTime : null;
        entry.Notes = TrimOrNull(Notes);

        db.SaveChanges();

        _appState.SetStatus(_entryId is int
            ? $"Phone issue updated for {entry.VehicleRegistration} ({entry.TeamName})."
            : $"Phone issued to {entry.TeamName} for {entry.VehicleRegistration}.");

        _onSaved();
        _close();
    }

    private void LoadLookupData()
    {
        try
        {
            using var db = new AppDbContext();

            var existingTeams = db.PhoneIssueLogEntries
                .AsNoTracking()
                .Select(x => x.TeamName)
                .ToList();
            var clientTeams = db.Clients
                .AsNoTracking()
                .Select(x => x.Name)
                .ToList();

            var teamNames = existingTeams
                .Concat(clientTeams)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeSingleLine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var billingRegistrations = db.BillingEntries
                .AsNoTracking()
                .Where(x => x.ArchivedAt == null)
                .Select(x => x.Registration)
                .ToList();
            var jobCardRegistrations = db.JobCards
                .AsNoTracking()
                .Select(x => x.Registration)
                .ToList();
            var existingPhoneLogRegistrations = db.PhoneIssueLogEntries
                .AsNoTracking()
                .Select(x => x.VehicleRegistration)
                .ToList();

            var vehicles = billingRegistrations
                .Concat(jobCardRegistrations)
                .Concat(existingPhoneLogRegistrations)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ReplaceOptions(TeamOptions, teamNames);
            ReplaceOptions(VehicleOptions, vehicles);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Phone issue lookup data failed to load: {ex.Message}", true);
            ErrorMessage = "Could not load team/vehicle lookup data.";
        }
    }

    private void LoadExisting(int entryId)
    {
        using var db = new AppDbContext();
        var entry = db.PhoneIssueLogEntries.FirstOrDefault(x => x.Id == entryId);
        if (entry is null)
        {
            ErrorMessage = "Phone issue record not found.";
            return;
        }

        TeamName = entry.TeamName;
        VehicleRegistration = entry.VehicleRegistration;
        TeamMemberOne = entry.TeamMemberOne;
        TeamMemberTwo = entry.TeamMemberTwo;
        PhoneLabel = entry.PhoneLabel;
        PhoneNumber = entry.PhoneNumber;
        PhoneImei = entry.PhoneImei;
        Notes = entry.Notes;
        IssuedAt = new DateTimeOffset(ToLocal(entry.IssuedAt));
        IsReturned = entry.ReturnedAt is not null;
        ReturnedAt = entry.ReturnedAt is DateTime returnedAt
            ? new DateTimeOffset(ToLocal(returnedAt))
            : null;

        AddOptionIfMissing(TeamOptions, TeamName);
        AddOptionIfMissing(VehicleOptions, VehicleRegistration);
    }

    private static void ReplaceOptions(ObservableCollection<string> target, System.Collections.Generic.IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value.Trim());
        }
    }

    private static void AddOptionIfMissing(ObservableCollection<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (target.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            return;

        target.Add(value.Trim());
    }

    private static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
