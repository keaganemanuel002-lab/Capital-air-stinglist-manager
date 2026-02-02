using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class JobCardRow : ObservableObject
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Company { get; set; } = "";
    public string Registration { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Imei { get; set; }
    public string? SerialNumber { get; set; }
    public string? Iccid { get; set; }
}

public partial class JobCardsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    public ObservableCollection<JobCardRow> Rows { get; } = new();

    [ObservableProperty] private JobCardRow? selectedRow;

    public JobCardsViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;
        Load();
    }

    public bool CanCompleteJobs => _appState.CanCompleteJobs;

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var items = db.JobCards.OrderByDescending(j => j.CreatedAt).ToList();

        Rows.Clear();
        foreach (var j in items)
        {
            Rows.Add(new JobCardRow
            {
                Id = j.Id,
                Type = j.Type.ToString(),
                Status = j.Status.ToString(),
                Company = j.Company,
                Registration = j.Registration,
                Make = j.Make,
                Model = j.Model,
                Imei = j.Imei,
                SerialNumber = j.SerialNumber,
                Iccid = j.Iccid
            });
        }
    }

    [RelayCommand]
    private async Task EditSelected()
    {
        if (SelectedRow is null) return;

        var dlg = new StingListManager.Views.JobCardEditWindow();
        dlg.DataContext = new JobCardEditViewModel(SelectedRow.Id, () => dlg.Close());

        await dlg.ShowDialog(_window);

        Load();
    }

    [RelayCommand]
    private void CompleteSelected()
    {
        if (!CanCompleteJobs) { _appState.SetStatus("Not permitted."); return; }
        if (SelectedRow is null) return;

        var wf = new WorkflowService();
        var result = wf.CompleteJobCard(SelectedRow.Id, _appState.OperatorName);
        _appState.SetStatus(result.message);
        Load();
    }

    [RelayCommand]
    private async Task OpenDocuments()
    {
        if (SelectedRow is null) return;

        var wnd = new StingListManager.Views.DocumentsWindow();
        var vm = new JobCardDocumentsViewModel(_window, _appState, SelectedRow.Id);

        var view = new StingListManager.Views.JobCardDocumentsView
        {
            DataContext = vm
        };

        wnd.Content = view;
        await wnd.ShowDialog(_window);
    }
}
