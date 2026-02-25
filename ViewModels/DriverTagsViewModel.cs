using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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

public partial class DriverTagRow : ObservableObject
{
    public int Id { get; set; }
    public string TagCode { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string IssuedDateDisplay { get; set; } = "";
    public string LostOrDamagedDateDisplay { get; set; } = "";
    public string EmploymentExitDisplay { get; set; } = "";
    public string ReturnStatusDisplay { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
    public bool IsLostOrDamaged { get; set; }
    public DriverEmploymentExitType EmploymentExitType { get; set; }
    public DriverTagReturnStatus ReturnStatus { get; set; }
}

public partial class DriverTagTransferRow : ObservableObject
{
    public string DateDisplay { get; set; } = "";
    public string FromDriverName { get; set; } = "";
    public string ToDriverName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string TransferredBy { get; set; } = "";
}

public partial class DriverTagsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private bool _suppressFilterReload;

    public ObservableCollection<DriverTagRow> Rows { get; } = new();
    public ObservableCollection<DriverTagTransferRow> TransferHistory { get; } = new();

    [ObservableProperty] private DriverTagRow? selectedRow;
    [ObservableProperty] private string? searchText;
    [ObservableProperty] private bool showClosed;

    public bool CanAmendSelected => SelectedRow is { Id: > 0 };
    public bool CanTransferSelected => SelectedRow is { Id: > 0 }
                                       && SelectedRow.EmploymentExitType == DriverEmploymentExitType.None
                                       && !SelectedRow.IsLostOrDamaged;
    public bool CanReportLostOrDamaged => SelectedRow is { Id: > 0 }
                                          && SelectedRow.EmploymentExitType == DriverEmploymentExitType.None;
    public bool CanClearLostOrDamaged => SelectedRow?.IsLostOrDamaged == true;
    public bool CanRecordExit => SelectedRow is { Id: > 0 };

    public DriverTagsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        EnsureSchema();
        Load();
    }

    partial void OnSelectedRowChanged(DriverTagRow? value)
    {
        OnPropertyChanged(nameof(CanAmendSelected));
        OnPropertyChanged(nameof(CanTransferSelected));
        OnPropertyChanged(nameof(CanReportLostOrDamaged));
        OnPropertyChanged(nameof(CanClearLostOrDamaged));
        OnPropertyChanged(nameof(CanRecordExit));
        LoadTransferHistory(value?.Id);
    }

    partial void OnSearchTextChanged(string? value)
    {
        if (_suppressFilterReload)
            return;

        Load();
    }

    partial void OnShowClosedChanged(bool value)
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

            IEnumerable<DriverTag> query = db.DriverTags
                .AsNoTracking()
                .OrderByDescending(x => x.IssuedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            if (!ShowClosed)
            {
                query = query.Where(x =>
                    x.EmploymentExitType == DriverEmploymentExitType.None
                    || x.ReturnStatus == DriverTagReturnStatus.NotReturned);
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                query = query.Where(x =>
                    Contains(x.TagCode, search)
                    || Contains(x.DriverName, search)
                    || Contains(x.LostOrDamagedReason, search)
                    || Contains(x.Notes, search));
            }

            var list = query.ToList();

            Rows.Clear();
            foreach (var item in list)
            {
                Rows.Add(new DriverTagRow
                {
                    Id = item.Id,
                    TagCode = item.TagCode,
                    DriverName = item.DriverName,
                    IssuedDateDisplay = ToLocal(item.IssuedAt).ToString("yyyy-MM-dd"),
                    LostOrDamagedDateDisplay = item.LostOrDamagedReportedAt is DateTime lostAt
                        ? ToLocal(lostAt).ToString("yyyy-MM-dd")
                        : string.Empty,
                    EmploymentExitDisplay = item.EmploymentExitType == DriverEmploymentExitType.None
                        ? string.Empty
                        : $"{item.EmploymentExitType} ({ToLocal(item.EmploymentExitAt ?? item.IssuedAt):yyyy-MM-dd})",
                    ReturnStatusDisplay = item.ReturnStatus switch
                    {
                        DriverTagReturnStatus.Returned => item.ReturnedAt is DateTime returnedAt
                            ? $"Returned ({ToLocal(returnedAt):yyyy-MM-dd})"
                            : "Returned",
                        DriverTagReturnStatus.NotReturned => "Not Returned",
                        _ => string.Empty
                    },
                    Status = ResolveStatus(item),
                    Notes = item.Notes,
                    IsLostOrDamaged = item.LostOrDamagedReportedAt is not null,
                    EmploymentExitType = item.EmploymentExitType,
                    ReturnStatus = item.ReturnStatus
                });
            }

            SelectedRow = selectedId is int id
                ? Rows.FirstOrDefault(x => x.Id == id) ?? Rows.FirstOrDefault()
                : Rows.FirstOrDefault();

            _appState.SetStatus($"Loaded {Rows.Count} driver tag records.");
        }
        catch (Exception ex)
        {
            Rows.Clear();
            TransferHistory.Clear();
            _appState.SetStatus($"Driver Tags failed to load: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task IssueTag()
    {
        try
        {
            var dialog = new DriverTagEditWindow();
            dialog.DataContext = new DriverTagEditViewModel(
                close: () => dialog.Close(),
                onSaved: Load,
                appState: _appState);
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Issue Driver Tag dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task AmendSelected()
    {
        if (!CanAmendSelected || SelectedRow is null)
            return;

        try
        {
            var dialog = new DriverTagEditWindow();
            dialog.DataContext = new DriverTagEditViewModel(
                selectedId: SelectedRow.Id,
                close: () => dialog.Close(),
                onSaved: Load,
                appState: _appState);
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Amend Driver Tag dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task TransferSelected()
    {
        if (!CanTransferSelected || SelectedRow is null)
        {
            if (SelectedRow is { IsLostOrDamaged: true })
                _appState.SetStatus("Cannot transfer a lost/damaged tag. Clear lost/damaged first.", true);
            return;
        }

        try
        {
            var dialog = new DriverTagTransferWindow();
            dialog.DataContext = new DriverTagTransferViewModel(
                driverTagId: SelectedRow.Id,
                currentDriverName: SelectedRow.DriverName,
                close: () => dialog.Close(),
                onSaved: Load,
                appState: _appState);
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Transfer Driver Tag dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task ReportLostOrDamaged()
    {
        if (!CanReportLostOrDamaged || SelectedRow is null)
            return;

        try
        {
            var dialog = new DriverTagLossWindow();
            dialog.DataContext = new DriverTagLossViewModel(
                driverTagId: SelectedRow.Id,
                tagCode: SelectedRow.TagCode,
                close: () => dialog.Close(),
                onSaved: Load,
                appState: _appState);
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Lost/Damaged dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ClearLostOrDamaged()
    {
        if (!CanClearLostOrDamaged || SelectedRow is null)
            return;

        using var db = new AppDbContext();
        var record = db.DriverTags.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (record is null)
        {
            _appState.SetStatus("Selected driver tag record was not found.", true);
            return;
        }

        record.LostOrDamagedReportedAt = null;
        record.LostOrDamagedReason = null;
        db.SaveChanges();
        _appState.SetStatus($"Cleared lost/damaged flag for tag {record.TagCode}.");
        Load();
    }

    [RelayCommand]
    private async Task RecordExit()
    {
        if (!CanRecordExit || SelectedRow is null)
            return;

        try
        {
            var dialog = new DriverTagExitWindow();
            dialog.DataContext = new DriverTagExitViewModel(
                driverTagId: SelectedRow.Id,
                tagCode: SelectedRow.TagCode,
                driverName: SelectedRow.DriverName,
                close: () => dialog.Close(),
                onSaved: Load,
                appState: _appState);
            await dialog.ShowDialog(_window);
        }
        catch (Exception ex)
        {
            _appState.SetStatus($"Could not open Employment Exit dialog: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressFilterReload = true;
        try
        {
            SearchText = null;
            ShowClosed = false;
        }
        finally
        {
            _suppressFilterReload = false;
        }

        Load();
    }

    private void LoadTransferHistory(int? driverTagId)
    {
        TransferHistory.Clear();
        if (driverTagId is not int id)
            return;

        using var db = new AppDbContext();
        var history = db.DriverTagTransfers
            .AsNoTracking()
            .Where(x => x.DriverTagId == id)
            .OrderByDescending(x => x.TransferredAt)
            .ToList();

        foreach (var item in history)
        {
            TransferHistory.Add(new DriverTagTransferRow
            {
                DateDisplay = ToLocal(item.TransferredAt).ToString("yyyy-MM-dd"),
                FromDriverName = item.FromDriverName,
                ToDriverName = item.ToDriverName,
                Reason = item.Reason,
                TransferredBy = string.IsNullOrWhiteSpace(item.TransferredBy) ? "-" : item.TransferredBy
            });
        }
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
            _appState.SetStatus($"Driver Tags schema check failed: {ex.Message}", true);
        }
    }

    private static string ResolveStatus(DriverTag row)
    {
        if (row.LostOrDamagedReportedAt is not null)
            return "Lost / Damaged";

        if (row.EmploymentExitType != DriverEmploymentExitType.None)
        {
            return row.ReturnStatus switch
            {
                DriverTagReturnStatus.Returned => "Exited - Returned",
                DriverTagReturnStatus.NotReturned => "Exited - Not Returned",
                _ => "Exited"
            };
        }

        return "Active";
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
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
