using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;
using StingListManager.Views;

namespace StingListManager.ViewModels;

public partial class PhoneIssueLogRow : ObservableObject
{
    public int Id { get; set; }
    public string TeamName { get; set; } = "";
    public string VehicleRegistration { get; set; } = "";
    public string TeamMemberOne { get; set; } = "";
    public string TeamMemberTwo { get; set; } = "";
    public string? PhoneLabel { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhoneImei { get; set; }
    public string? PhoneImeiSecondary { get; set; }
    public string IssuedAtDisplay { get; set; } = "";
    public string ReturnedAtDisplay { get; set; } = "";
    public string Status { get; set; } = "Issued";
    public string? RepairDetails { get; set; }
    public string? Notes { get; set; }
    public int InvoiceCount { get; set; }
    public bool IsReturned { get; set; }
}

public partial class PhoneIssueLogViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private bool _suppressFilterReload;

    public ObservableCollection<PhoneIssueLogRow> Rows { get; } = new();
    public ObservableCollection<string> AvailableTeams { get; } = new();
    public ObservableCollection<string> AvailableVehicles { get; } = new();

    [ObservableProperty] private PhoneIssueLogRow? selectedRow;
    [ObservableProperty] private string selectedTeam = "All Teams";
    [ObservableProperty] private string selectedVehicle = "All Vehicles";
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool showReturned;

    public bool CanEditSelected => SelectedRow is { Id: > 0 };
    public bool CanMarkReturned => SelectedRow is { Id: > 0, IsReturned: false };
    public bool CanDeleteSelected => SelectedRow is { Id: > 0 };
    public bool CanManageInvoices => SelectedRow is { Id: > 0 };

    public PhoneIssueLogViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        EnsureSchema();
        Load();
    }

    partial void OnSelectedRowChanged(PhoneIssueLogRow? value)
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanMarkReturned));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanManageInvoices));
    }

    partial void OnSelectedTeamChanged(string value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    partial void OnSelectedVehicleChanged(string value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    partial void OnSearchTextChanged(string? value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    partial void OnShowReturnedChanged(bool value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            var selectedId = SelectedRow?.Id;

            using var db = new AppDbContext();
            var allEntries = db.PhoneIssueLogEntries
                .AsNoTracking()
                .OrderByDescending(x => x.IssuedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            var invoiceCounts = db.Attachments
                .AsNoTracking()
                .Where(a => a.OwnerType == AttachmentOwnerType.PhoneIssue
                            && a.Kind == AttachmentKind.Invoice)
                .GroupBy(a => a.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.OwnerId, x => x.Count);

            var billingRegistrations = db.BillingEntries
                .AsNoTracking()
                .Where(x => x.ArchivedAt == null)
                .Select(x => x.Registration)
                .ToList();

            var jobCardRegistrations = db.JobCards
                .AsNoTracking()
                .Select(x => x.Registration)
                .ToList();

            var knownVehicles = billingRegistrations
                .Concat(jobCardRegistrations)
                .Concat(allEntries.Select(x => x.VehicleRegistration))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            RefreshFilterOptions(allEntries, knownVehicles);

            IEnumerable<PhoneIssueLogEntry> filtered = allEntries;

            if (!ShowReturned)
                filtered = filtered.Where(x => x.ReturnedAt == null);

            if (!string.Equals(SelectedTeam, "All Teams", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x =>
                    string.Equals(x.TeamName, SelectedTeam, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(SelectedVehicle, "All Vehicles", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x =>
                    string.Equals(x.VehicleRegistration, SelectedVehicle, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                filtered = filtered.Where(x =>
                    Contains(x.TeamName, search)
                    || Contains(x.VehicleRegistration, search)
                    || Contains(x.TeamMemberOne, search)
                    || Contains(x.TeamMemberTwo, search)
                    || Contains(x.PhoneLabel, search)
                    || Contains(x.PhoneNumber, search)
                    || Contains(x.PhoneImei, search)
                    || Contains(x.PhoneImeiSecondary, search)
                    || Contains(x.RepairDetails, search)
                    || Contains(x.Notes, search));
            }

            var visible = filtered.ToList();

            Rows.Clear();
            foreach (var entry in visible)
            {
                Rows.Add(new PhoneIssueLogRow
                {
                    Id = entry.Id,
                    TeamName = entry.TeamName,
                    VehicleRegistration = entry.VehicleRegistration,
                    TeamMemberOne = entry.TeamMemberOne,
                    TeamMemberTwo = entry.TeamMemberTwo,
                    PhoneLabel = entry.PhoneLabel,
                    PhoneNumber = entry.PhoneNumber,
                    PhoneImei = entry.PhoneImei,
                    PhoneImeiSecondary = entry.PhoneImeiSecondary,
                    IssuedAtDisplay = ToLocal(entry.IssuedAt).ToString("yyyy-MM-dd"),
                    ReturnedAtDisplay = entry.ReturnedAt is DateTime returnedAt
                        ? ToLocal(returnedAt).ToString("yyyy-MM-dd")
                        : string.Empty,
                    Status = entry.ReturnedAt is null ? "Issued" : "Returned",
                    RepairDetails = entry.RepairDetails,
                    Notes = entry.Notes,
                    InvoiceCount = invoiceCounts.TryGetValue(entry.Id, out var count) ? count : 0,
                    IsReturned = entry.ReturnedAt is not null
                });
            }

            SelectedRow = selectedId is int id ? Rows.FirstOrDefault(x => x.Id == id) : Rows.FirstOrDefault();

            var returnedSuffix = ShowReturned ? " (including returned)" : " (active only)";
            _appState.SetStatus($"Loaded {visible.Count} phone issue entries{returnedSuffix}.");
        }
        catch (Exception ex)
        {
            Rows.Clear();
            _appState.SetStatus($"Phone Issue Log failed to load: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task IssuePhone()
    {
        try
        {
            var dialog = new PhoneIssueEditWindow();
            dialog.DataContext = new PhoneIssueEditViewModel(
                () => dialog.Close(),
                Load,
                _appState);

            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Issue Phone dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (!CanEditSelected || SelectedRow is null)
            return;

        try
        {
            var dialog = new PhoneIssueEditWindow();
            dialog.DataContext = new PhoneIssueEditViewModel(
                SelectedRow.Id,
                () => dialog.Close(),
                Load,
                _appState);

            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Amend Issue dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task ManageInvoices()
    {
        if (!CanManageInvoices || SelectedRow is null)
            return;

        try
        {
            var wnd = new DocumentsWindow
            {
                Title = "Phone Issue Invoices",
                Width = 820,
                Height = 620
            };

            var vm = new PhoneIssueDocumentsViewModel(_window, _appState, SelectedRow.Id);
            var view = new PhoneIssueDocumentsView { DataContext = vm };
            wnd.Content = view;
            await wnd.ShowDialog(_window);
            Load();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Phone Issue invoices: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void MarkReturned()
    {
        if (!CanMarkReturned || SelectedRow is null)
            return;

        using var db = new AppDbContext();
        var entry = db.PhoneIssueLogEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (entry is null)
        {
            _appState.SetStatus("Selected phone issue entry was not found.", true);
            return;
        }

        entry.ReturnedAt = DateTime.UtcNow;
        db.SaveChanges();

        _appState.SetStatus($"Phone marked returned for {entry.VehicleRegistration}.");
        Load();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (!CanDeleteSelected || SelectedRow is null)
            return;

        using var db = new AppDbContext();
        var entry = db.PhoneIssueLogEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (entry is null)
        {
            _appState.SetStatus("Selected phone issue entry was not found.", true);
            return;
        }

        db.PhoneIssueLogEntries.Remove(entry);
        db.SaveChanges();
        _appState.SetStatus("Phone issue entry deleted.");
        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressFilterReload = true;
        try
        {
            SearchText = null;
            SelectedTeam = "All Teams";
            SelectedVehicle = "All Vehicles";
            ShowReturned = false;
        }
        finally
        {
            _suppressFilterReload = false;
        }

        Load();
    }

    private void RefreshFilterOptions(
        IReadOnlyCollection<PhoneIssueLogEntry> allEntries,
        IReadOnlyCollection<string> knownVehicles)
    {
        var teams = allEntries
            .Select(x => x.TeamName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        _suppressFilterReload = true;
        try
        {
            AvailableTeams.Clear();
            AvailableTeams.Add("All Teams");
            foreach (var team in teams)
                AvailableTeams.Add(team);

            AvailableVehicles.Clear();
            AvailableVehicles.Add("All Vehicles");
            foreach (var vehicle in knownVehicles)
                AvailableVehicles.Add(vehicle);

            if (!AvailableTeams.Any(x => string.Equals(x, SelectedTeam, StringComparison.OrdinalIgnoreCase)))
                SelectedTeam = "All Teams";

            if (!AvailableVehicles.Any(x => string.Equals(x, SelectedVehicle, StringComparison.OrdinalIgnoreCase)))
                SelectedVehicle = "All Vehicles";
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureSchema()
    {
        try
        {
            using var db = new AppDbContext();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Phone Issue Log schema check failed: {ex.Message}", true);
        }
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
