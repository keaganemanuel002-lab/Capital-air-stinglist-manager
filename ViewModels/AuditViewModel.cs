using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;

namespace StingListManager.ViewModels;

public partial class AuditRow : ObservableObject
{
    public int Id { get; set; }
    public DateTime At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? Registration { get; set; }
    public string? Details { get; set; }
}

public partial class AuditViewModel : ViewModelBase
{
    public ObservableCollection<AuditRow> Rows { get; } = new();

    public AuditViewModel()
    {
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var items = db.AuditEvents
            .OrderByDescending(a => a.At)
            .Take(500)
            .ToList();

        Rows.Clear();
        foreach (var ae in items)
        {
            Rows.Add(new AuditRow
            {
                Id = ae.Id,
                At = ae.At,
                Actor = ae.Actor,
                Action = ae.Action,
                EntityType = ae.EntityType,
                Registration = ae.Registration,
                Details = ae.Details
            });
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Load();
    }
}
