using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class RemovalRow : ObservableObject
{
    public int Id { get; set; }
    public string Client { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? FleetNumber { get; set; }
    public string? MakeModel { get; set; }
    public string Status { get; set; } = "";
    public string DateReceived { get; set; } = "";
    public string? Reason { get; set; }
}

public partial class RemovalsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    public ObservableCollection<RemovalRow> Rows { get; } = new();

    [ObservableProperty] private RemovalRow? selectedRow;

    public RemovalsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();

        var items = db.CancellationEntries
            .OrderByDescending(c => c.DateRequestReceived)
            .ThenByDescending(c => c.Id)
            .ToList();

        Rows.Clear();
        foreach (var c in items)
        {
            Rows.Add(new RemovalRow
            {
                Id = c.Id,
                Client = c.Client,
                Registration = c.Registration,
                FleetNumber = c.FleetNumber,
                MakeModel = c.MakeModel,
                Status = c.Status.ToString(),
                DateReceived = c.DateRequestReceived?.ToString("yyyy-MM-dd") ?? "",
                Reason = c.Reason
            });
        }

        _appState.SetStatus($"Loaded {Rows.Count} removal requests.");
    }

    [RelayCommand]
    private async Task AddRequest()
    {
        var dlg = new StingListManager.Views.RemovalRequestEditWindow();
        dlg.DataContext = new RemovalRequestEditViewModel(null, () => dlg.Close());

        await dlg.ShowDialog(_window);
        Load();
        _appState.SetStatus("Removal request added.");
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.RemovalRequestEditWindow();
        dlg.DataContext = new RemovalRequestEditViewModel(SelectedRow.Id, () => dlg.Close());

        await dlg.ShowDialog(_window);
        Load();
        _appState.SetStatus("Removal request updated.");
    }

    [RelayCommand]
    private void CreateRemovalQuote()
    {
        if (SelectedRow is null) return;

        using var db = new AppDbContext();
        var c = db.CancellationEntries.FirstOrDefault(x => x.Id == SelectedRow.Id);
        if (c is null) return;

        // Create removal quote linked to this cancellation
        var q = new Quote
        {
            Type = QuoteType.Removal,
            Status = QuoteStatus.Draft,
            Company = c.Client,
            Registration = c.Registration,
            FleetNumber = c.FleetNumber,
            AmountExVat = 0m,
            Notes = $"Removal request for {c.Registration}"
        };

        db.Quotes.Add(q);
        db.SaveChanges();

        c.QuoteId = q.Id;
        c.Status = CancellationStatus.Quoted;
        db.SaveChanges();

        _appState.SetStatus("Removal quote created (Draft). Approve it under Quotes.");
        Load();
    }
}
