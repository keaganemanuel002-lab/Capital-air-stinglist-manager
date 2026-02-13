using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class DashcamsViewModel : ViewModelBase
{
    public ObservableCollection<Dashcam> Dashcams { get; } = new();
    public ObservableCollection<Dashcam> FilteredDashcams { get; } = new();
    public ObservableCollection<SdCard> SdCards { get; } = new();
    public int[] SdSlots { get; } = new[] { 1, 2 };

    [ObservableProperty]
    private Dashcam? _selected;

    [ObservableProperty]
    private SdCard? _selectedSdCard;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public DashcamsViewModel()
    {
        Load();
    }

    [RelayCommand]
    public void Load()
    {
        Dashcams.Clear();
        using var db = new AppDbContext();
        var list = db.Dashcams.OrderBy(d => d.Id).ToList();
        foreach (var d in list) Dashcams.Add(d);
        ApplyDashcamFilter();
        SdCards.Clear();
    }

    [RelayCommand]
    public void AddNew()
    {
        var d = new Dashcam { PurchasedAt = DateTime.Now };
        using var db = new AppDbContext();
        db.Dashcams.Add(d);
        db.SaveChanges();
        Dashcams.Add(d);
        ApplyDashcamFilter();
        Selected = d;
    }

    partial void OnSelectedChanged(Dashcam? value)
    {
        // load sd cards for selected dashcam
        SdCards.Clear();
        if (value is null) return;
        using var db = new AppDbContext();
        var cards = db.SdCards
            .Where(s => s.DashcamId == value.Id)
            .ToList()
            .OrderBy(s => s.SlotNumber)
            .ThenByDescending(s => s.InstalledAt ?? DateTimeOffset.MinValue)
            .ToList();
        foreach (var c in cards) SdCards.Add(c);
        SelectedSdCard = SdCards.FirstOrDefault();
    }

    [RelayCommand]
    public async Task AddSdCard()
    {
        if (Selected is null) return;
        
        var activeCardCount = SdCards.Count(s => s.ChangedAt is null);
        if (activeCardCount >= 2)
        {
            await DialogService.Alert(
                "SD Card Limit Reached",
                "Each dashcam can only have 2 active SD cards. Set a Changed At date on an existing card before adding a replacement.");
            return;
        }

        var c = new SdCard
        {
            DashcamId = Selected.Id,
            SlotNumber = GetNextAvailableSlot(),
            InstalledAt = DateTime.Now,
            InstalledInVehicleRegistration = Selected.AllocatedVehicleRegistration
        };

        using var db = new AppDbContext();
        db.SdCards.Add(c);
        db.SaveChanges();
        SdCards.Add(c);
        SelectedSdCard = c;
    }

    [RelayCommand]
    public async Task SaveSdCard()
    {
        if (SelectedSdCard is null) return;
        if (SelectedSdCard.SlotNumber is < 1 or > 2)
        {
            await DialogService.Alert("Invalid SD Slot", "Slot number must be 1 or 2.");
            return;
        }

        if (SelectedSdCard.InstalledAt is not null &&
            SelectedSdCard.ChangedAt is not null &&
            SelectedSdCard.ChangedAt < SelectedSdCard.InstalledAt)
        {
            await DialogService.Alert("Invalid Dates", "Changed At cannot be earlier than Installed At.");
            return;
        }

        var otherActiveCards = SdCards
            .Where(s => s.Id != SelectedSdCard.Id && s.ChangedAt is null)
            .ToList();

        if (SelectedSdCard.ChangedAt is null && otherActiveCards.Count >= 2)
        {
            await DialogService.Alert("Too Many Active SD Cards", "A dashcam can only have 2 active SD cards.");
            return;
        }

        if (SelectedSdCard.ChangedAt is null &&
            otherActiveCards.Any(s => s.SlotNumber == SelectedSdCard.SlotNumber))
        {
            await DialogService.Alert(
                "Slot Already In Use",
                $"Slot {SelectedSdCard.SlotNumber} already has an active SD card. Mark the existing card as changed first.");
            return;
        }

        try
        {
            using var db = new AppDbContext();
            db.SdCards.Update(SelectedSdCard);
            db.SaveChanges();
            OnSelectedChanged(Selected);
        }
        catch (Exception ex)
        {
            await DialogService.Alert("Save SD Card Failed", ex.Message);
        }
    }

    [RelayCommand]
    public void DeleteSdCard()
    {
        if (SelectedSdCard is null) return;
        using var db = new AppDbContext();
        db.SdCards.Remove(SelectedSdCard);
        db.SaveChanges();
        SdCards.Remove(SelectedSdCard);
        SelectedSdCard = null;
    }

    [RelayCommand]
    public async Task SaveSelected()
    {
        if (Selected is null) return;
        try
        {
            using var db = new AppDbContext();
            db.Dashcams.Update(Selected);
            db.SaveChanges();
            ApplyDashcamFilter();
            OnSelectedChanged(Selected);
        }
        catch (Exception ex)
        {
            await DialogService.Alert("Save Dashcam Failed", ex.Message);
        }
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (Selected is null) return;
        using var db = new AppDbContext();
        db.Dashcams.Remove(Selected);
        db.SaveChanges();
        Dashcams.Remove(Selected);
        ApplyDashcamFilter();
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyDashcamFilter();
    }

    private void ApplyDashcamFilter()
    {
        var currentSelection = Selected;
        var query = Dashcams.AsEnumerable();
        var term = SearchText.Trim();

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(d =>
                Matches(d.SerialNumber, term) ||
                Matches(d.Model, term) ||
                Matches(d.AllocatedVehicleRegistration, term) ||
                Matches(d.Notes, term));
        }

        FilteredDashcams.Clear();
        foreach (var dashcam in query)
        {
            FilteredDashcams.Add(dashcam);
        }

        if (currentSelection is null) return;
        if (!FilteredDashcams.Contains(currentSelection))
        {
            Selected = FilteredDashcams.FirstOrDefault();
        }
    }

    private static bool Matches(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private int GetNextAvailableSlot()
    {
        var hasSlot1Active = SdCards.Any(s => s.SlotNumber == 1 && s.ChangedAt is null);
        if (!hasSlot1Active) return 1;

        var hasSlot2Active = SdCards.Any(s => s.SlotNumber == 2 && s.ChangedAt is null);
        if (!hasSlot2Active) return 2;

        return 1;
    }
}
