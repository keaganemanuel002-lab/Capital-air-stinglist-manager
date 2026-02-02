using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class SearchViewModel : ViewModelBase
{
    private readonly AppState _appState;
    private readonly Action<SearchResult> _open;
    private readonly Action<SearchResult> _startRemoval;
    private readonly Action<SearchResult> _openDocs;

    public ObservableCollection<SearchResult> Results { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private SearchResult? selected;

    public SearchViewModel(AppState appState, Action<SearchResult> open,
                           Action<SearchResult> startRemoval,
                           Action<SearchResult> openDocs)
    {
        _appState = appState;
        _open = open;
        _startRemoval = startRemoval;
        _openDocs = openDocs;
    }

    [RelayCommand]
    private void RunSearch()
    {
        Results.Clear();
        foreach (var r in new SearchService(_appState.Settings).Search(Query))
            Results.Add(r);

        _appState.SetStatus($"Search results: {Results.Count}");
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is null) return;
        _open(Selected);
    }

    [RelayCommand]
    private void StartRemovalSelected()
    {
        if (Selected is null) return;
        _startRemoval(Selected);
    }

    [RelayCommand]
    private void OpenDocsSelected()
    {
        if (Selected is null) return;
        _openDocs(Selected);
    }
}
