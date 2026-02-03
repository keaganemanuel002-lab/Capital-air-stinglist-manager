using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data.Entities;
using StingListManager.Services;
using StingListManager.Views;
using StingListManager.ViewModels;

namespace StingListManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    [ObservableProperty]
    private ViewModelBase currentPage;

    [ObservableProperty]
    private int navIndex;

    public string StatusMessage => _appState.StatusMessage;
    public string StatusTime => _appState.StatusTime;

    public string CurrentPageName => CurrentPage?.GetType().Name ?? "DashboardViewModel";
    public bool CanImportExcel => CurrentPage is StingListViewModel;

    public MainWindowViewModel(Window window)
    {
        _window = window;
        _appState = new AppState();

        // Bubble state changes to UI
        _appState.PropertyChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(StatusTime));
        };

        CurrentPage = new SearchViewModel(_appState, OpenResult, StartRemovalFromResult, OpenDocsFromResult);
    }

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(CurrentPageName));
        OnPropertyChanged(nameof(CanImportExcel));
    }

    partial void OnNavIndexChanged(int value)
    {
        switch (value)
        {
            case 0: CurrentPage = new SearchViewModel(_appState, OpenResult, StartRemovalFromResult, OpenDocsFromResult); break;
            case 1: CurrentPage = new DashboardViewModel(_appState, NavigateFromDashboard); break;
            case 2: CurrentPage = new QuotesViewModel(_window, _appState, NavigateToJobCards); break;
            case 3: CurrentPage = new InstallationsViewModel(_window, _appState); break;
            case 4: CurrentPage = new JobCardsViewModel(_window, _appState); break;
            case 5: CurrentPage = new RemovalsViewModel(_window, _appState); break;
            case 6: CurrentPage = new StingListViewModel(_window, _appState); break;
            case 7: CurrentPage = new BillingListViewModel(_window, _appState); break;
            case 8: CurrentPage = new ClientsViewModel(_appState); break;
            case 9: CurrentPage = new ExportViewModel(_window, _appState); break;
            case 10: CurrentPage = new SettingsViewModel(_window, _appState); break;
        }
    }

    private void NavigateToJobCards()
    {
        NavIndex = 4; // Job Cards
        CurrentPage = new JobCardsViewModel(_window, _appState);
    }

    private void NavigateFromDashboard(DashboardNavRequest request)
    {
        switch (request.Target)
        {
            case DashboardNavTarget.Quotes:
            case DashboardNavTarget.QuoteValue:
                NavIndex = 2;
                CurrentPage = new QuotesViewModel(_window, _appState, NavigateToJobCards, request.StartDate, request.EndDate);
                break;
            case DashboardNavTarget.JobCards:
                NavIndex = 4;
                CurrentPage = new JobCardsViewModel(_window, _appState, request.StartDate, request.EndDate);
                break;
            case DashboardNavTarget.RemovalRequests:
                NavIndex = 5;
                CurrentPage = new RemovalsViewModel(_window, _appState, request.StartDate, request.EndDate);
                break;
            case DashboardNavTarget.ActiveBilling:
                NavIndex = 6;
                CurrentPage = new StingListViewModel(_window, _appState, request.StartDate, request.EndDate, BillingStatus.Active.ToString());
                break;
        }
    }

    private void OpenResult(Services.SearchResult result)
    {
        switch (result.Type)
        {
            case Services.SearchResultType.BillingEntry:
                NavIndex = 6; // STING List
                break;
            case Services.SearchResultType.Quote:
                NavIndex = 2; // Quotes
                break;
            case Services.SearchResultType.JobCard:
                NavIndex = 4; // Job Cards
                break;
            case Services.SearchResultType.Cancellation:
                NavIndex = 5; // Removals
                break;
        }
    }

    private void StartRemovalFromResult(Services.SearchResult result)
    {
        if (result.Type != Services.SearchResultType.BillingEntry)
        {
            _appState.SetStatus("Start Removal only works for STING List entries.");
            return;
        }

        using var db = new Data.AppDbContext();
        var entry = db.BillingEntries.FirstOrDefault(b => b.Id == result.Id);
        if (entry is null)
        {
            _appState.SetStatus("Billing entry not found.");
            return;
        }

        // Create removal quote prefilled from billing entry
        db.Quotes.Add(new Data.Entities.Quote
        {
            Type = Data.Entities.QuoteType.Removal,
            Status = Data.Entities.QuoteStatus.Draft,
            Company = entry.Company,
            Registration = entry.Registration,
            FleetNumber = entry.FleetNumber,
            AmountExVat = 0m,
            Notes = $"Removal for unit: {entry.Registration}"
        });

        db.SaveChanges();
        _appState.SetStatus("Removal quote created. Navigate to Quotes to approve.");
        NavIndex = 2; // Go to Quotes
    }

    private async void OpenDocsFromResult(Services.SearchResult result)
    {
        if (result.Type == Services.SearchResultType.Quote)
        {
            var wnd = new Views.DocumentsWindow();
            var vm = new QuoteDocumentsViewModel(_window, _appState, result.Id);
            var view = new Views.QuoteDocumentsView { DataContext = vm };
            wnd.Content = view;
            await wnd.ShowDialog(_window);
        }
        else if (result.Type == Services.SearchResultType.JobCard)
        {
            var wnd = new Views.DocumentsWindow();
            var vm = new JobCardDocumentsViewModel(_window, _appState, result.Id);
            var view = new Views.JobCardDocumentsView { DataContext = vm };
            wnd.Content = view;
            await wnd.ShowDialog(_window);
        }
        else
        {
            _appState.SetStatus("Documents only available for Quotes and Job Cards.");
        }
    }

    private void SetStatus(string message) => _appState.SetStatus(message);

    [RelayCommand]
    private void GoSearch() => NavIndex = 0;

    [RelayCommand]
    private void GoExport() => NavIndex = 9;

    [RelayCommand]
    private void SaveSettings()
    {
        _appState.SaveSettings();
        _appState.SetStatus("Settings saved (Ctrl+S).");
    }

    [RelayCommand]
    private async Task ImportExcel()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select STING billing Excel file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Excel files")
                {
                    Patterns = ["*.xlsx", "*.xlsm"]
                }
            ]
        });

        var file = files?.FirstOrDefault();
        if (file is null) return;

        var path = file.Path.LocalPath;

        var importer = new ExcelImportService();
        importer.ImportBillingAndCancellations(path, _appState.OperatorName);

        _appState.SetStatus($"Imported: {Path.GetFileName(path)}");
        NavIndex = 6; // Go to STING List
    }

    [RelayCommand]
    private async Task OpenProductCatalog()
    {
        var wnd = new ProductCatalogWindow();
        wnd.DataContext = new ProductCatalogViewModel(_appState);
        await wnd.ShowDialog(_window);
        _appState.SetStatus("Product catalog updated.");
    }
}
