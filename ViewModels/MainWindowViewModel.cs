using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data.Entities;
using StingListManager.Services;
using StingListManager.Views;
using StingListManager.ViewModels;

namespace StingListManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int NotificationRetentionHours = 48;
    private const int ToastDurationSeconds = 5;
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly TechnicianApiHostService _technicianApiHost = TechnicianApiHostService.Instance;
    private readonly FirebaseSyncService _firebaseSyncService = FirebaseSyncService.Instance;
    private readonly string _notificationStorePath;
    private readonly ObservableCollection<AppNotificationItem> _notifications = new();
    private CancellationTokenSource? _toastCts;
    private string? _lastNotificationMessage;
    private DateTime _lastNotificationUtc = DateTime.MinValue;
    private bool _isDisposing;

    [ObservableProperty]
    private ViewModelBase currentPage;

    [ObservableProperty]
    private int navIndex;

    [ObservableProperty]
    private bool isNotificationHistoryOpen;

    [ObservableProperty]
    private bool isToastVisible;

    [ObservableProperty]
    private AppNotificationItem? activeToast;

    [ObservableProperty]
    private int unreadNotificationCount;

    public string StatusMessage => _appState.StatusMessage;
    public string StatusTime => _appState.StatusTime;

    public string CurrentPageName => CurrentPage?.GetType().Name ?? "DashboardViewModel";
    public bool CanImportExcel => CurrentPage is StingListViewModel;
    public string SignedInAs => $"{_appState.OperatorName} ({_appState.Role})";
    public ObservableCollection<AppNotificationItem> Notifications => _notifications;
    public bool HasNotificationHistory => Notifications.Count > 0;
    public bool HasNoNotificationHistory => !HasNotificationHistory;
    public bool HasUnreadNotifications => UnreadNotificationCount > 0;
    public string UnreadNotificationBadge => UnreadNotificationCount > 99 ? "99+" : UnreadNotificationCount.ToString();

    public MainWindowViewModel(Window window, string? signedInUser = null, string? signedInRole = null)
    {
        _window = window;
        _appState = new AppState();
        _notificationStorePath = Path.Combine(Paths.BaseDir, "notifications.json");
        if (!string.IsNullOrWhiteSpace(signedInUser))
        {
            _appState.OperatorName = signedInUser.Trim();
            _appState.Role = string.IsNullOrWhiteSpace(signedInRole) ? _appState.Role : signedInRole.Trim();
            _appState.SaveSettings();
        }

        LoadNotificationHistory();

        _technicianApiHost.TechnicianNotification += OnTechnicianNotification;

        // Bubble state changes to UI
        _appState.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(StatusTime));

            if (e.PropertyName == nameof(AppState.StatusMessage))
            {
                PublishNotification(_appState.StatusMessage, _appState.StatusIsError);
            }
        };

        CurrentPage = new SearchViewModel(_appState, OpenResult, StartRemovalFromResult, OpenDocsFromResult);
        _ = StartTechnicianApiAsync();
        _ = StartFirebaseSyncAsync();
    }

    public void Dispose()
    {
        if (_isDisposing)
            return;

        _isDisposing = true;
        _technicianApiHost.TechnicianNotification -= OnTechnicianNotification;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        SaveNotificationHistory();

        // Do not block UI thread during window close; blocking here can deadlock shutdown.
        _ = Task.Run(async () =>
        {
            try
            {
                await _technicianApiHost.StopAsync();
                await _firebaseSyncService.StopAsync();
            }
            catch
            {
                // Ignore shutdown failures during app close.
            }
        });
    }

    private async Task StartTechnicianApiAsync()
    {
        var (started, message) = await _technicianApiHost.StartAsync(_appState.Settings);
        if (!started)
        {
            if (_appState.Settings.TechnicianApiEnabled)
                _appState.SetStatus(message, true);
            return;
        }

        var preferredUrl = TechnicianApiHostService.GetSuggestedPortalUrls(_appState.Settings.TechnicianApiPort)
            .FirstOrDefault(url => !url.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            ?? $"http://localhost:{_appState.Settings.TechnicianApiPort}/technician";

        _appState.SetStatus($"Technician portal ready: {preferredUrl}");
    }

    private async Task StartFirebaseSyncAsync()
    {
        var (started, message) = await _firebaseSyncService.StartAsync(
            _appState.Settings,
            (status, isError) => _appState.SetStatus(status, isError));

        if (!started)
        {
            if (_appState.Settings.FirebaseSyncEnabled)
                _appState.SetStatus(message, true);
            return;
        }

        _appState.SetStatus(message);
    }

    partial void OnUnreadNotificationCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadNotifications));
        OnPropertyChanged(nameof(UnreadNotificationBadge));
    }

    partial void OnIsNotificationHistoryOpenChanged(bool value)
    {
        if (!value)
            return;

        UnreadNotificationCount = 0;
        PruneExpiredNotifications();
        SaveNotificationHistory();
        OnPropertyChanged(nameof(HasNotificationHistory));
        OnPropertyChanged(nameof(HasNoNotificationHistory));
    }

    [RelayCommand]
    private void ToggleNotificationHistory()
    {
        IsNotificationHistoryOpen = !IsNotificationHistoryOpen;
    }

    [RelayCommand]
    private void CloseNotificationHistory()
    {
        IsNotificationHistoryOpen = false;
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastCts?.Cancel();
        IsToastVisible = false;
    }

    [RelayCommand]
    private void ClearNotificationHistory()
    {
        Notifications.Clear();
        UnreadNotificationCount = 0;
        SaveNotificationHistory();
        OnPropertyChanged(nameof(HasNotificationHistory));
        OnPropertyChanged(nameof(HasNoNotificationHistory));
    }

    private void PublishNotification(string? message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var trimmedMessage = message.Trim();
        if (string.Equals(_lastNotificationMessage, trimmedMessage, StringComparison.Ordinal)
            && DateTime.UtcNow - _lastNotificationUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastNotificationMessage = trimmedMessage;
        _lastNotificationUtc = DateTime.UtcNow;

        var notification = new AppNotificationItem
        {
            Title = isError ? "Error" : "Status",
            Message = trimmedMessage,
            CreatedAt = DateTimeOffset.UtcNow,
            IsError = isError
        };

        Notifications.Insert(0, notification);
        PruneExpiredNotifications();
        SaveNotificationHistory();
        OnPropertyChanged(nameof(HasNotificationHistory));
        OnPropertyChanged(nameof(HasNoNotificationHistory));

        if (!IsNotificationHistoryOpen)
            UnreadNotificationCount++;

        ShowToast(notification);
    }

    private void ShowToast(AppNotificationItem notification)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();

        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ActiveToast = notification;
        IsToastVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ToastDurationSeconds), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                    IsToastVisible = false;
            });
        });
    }

    private void LoadNotificationHistory()
    {
        try
        {
            Notifications.Clear();

            if (File.Exists(_notificationStorePath))
            {
                var json = File.ReadAllText(_notificationStorePath);
                var data = JsonSerializer.Deserialize<List<StoredNotification>>(json) ?? new List<StoredNotification>();
                var cutoff = DateTimeOffset.UtcNow.AddHours(-NotificationRetentionHours);

                foreach (var item in data
                    .Where(x => x.CreatedAt >= cutoff)
                    .OrderByDescending(x => x.CreatedAt))
                {
                    Notifications.Add(new AppNotificationItem
                    {
                        Title = string.IsNullOrWhiteSpace(item.Title) ? "Status" : item.Title!,
                        Message = item.Message ?? string.Empty,
                        CreatedAt = item.CreatedAt,
                        IsError = item.IsError
                    });
                }
            }
        }
        catch
        {
            // Ignore notification history load failures.
        }

        UnreadNotificationCount = 0;
        OnPropertyChanged(nameof(HasNotificationHistory));
        OnPropertyChanged(nameof(HasNoNotificationHistory));
    }

    private void SaveNotificationHistory()
    {
        try
        {
            Paths.Ensure();
            var cutoff = DateTimeOffset.UtcNow.AddHours(-NotificationRetentionHours);
            var data = Notifications
                .Where(x => x.CreatedAt >= cutoff)
                .Take(500)
                .Select(x => new StoredNotification
                {
                    Title = x.Title,
                    Message = x.Message,
                    CreatedAt = x.CreatedAt,
                    IsError = x.IsError
                })
                .ToList();

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            File.WriteAllText(_notificationStorePath, json);
        }
        catch
        {
            // Ignore notification history save failures.
        }
    }

    private void PruneExpiredNotifications()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-NotificationRetentionHours);
        var stale = Notifications.Where(x => x.CreatedAt < cutoff).ToList();
        foreach (var item in stale)
        {
            Notifications.Remove(item);
        }

        while (Notifications.Count > 500)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }
    }

    private sealed class StoredNotification
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsError { get; set; }
    }

    private void OnTechnicianNotification(string message)
    {
        _appState.SetStatus(message);
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
            case 9: CurrentPage = new UsersViewModel(_appState); break;
            case 10: CurrentPage = new ExportViewModel(_window, _appState); break;
            case 11: CurrentPage = new SettingsViewModel(_window, _appState); break;
            case 12: CurrentPage = new WialonReportsViewModel(_window, _appState); break;
            case 13: CurrentPage = new DashcamsViewModel(); break;
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
                CurrentPage = new StingListViewModel(_window, _appState, request.StartDate, request.EndDate, "Current");
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
            QuoteNumber = QuoteNumberAllocator.GetNext(db),
            Type = Data.Entities.QuoteType.Removal,
            Status = Data.Entities.QuoteStatus.Draft,
            Company = entry.Company,
            Registration = entry.Registration,
            FleetNumber = entry.FleetNumber,
            AmountExVat = _appState.Settings.DefaultRemovalFeeExVat,
            LineItems = new System.Collections.Generic.List<Data.Entities.QuoteLineItem>
            {
                new()
                {
                    LineNumber = 1,
                    ProductType = "Removal Fee",
                    ProductCode = "AUTO-REMOVAL-FEE",
                    ProductName = "Removal Fee",
                    Quantity = 1,
                    UnitPriceExVat = _appState.Settings.DefaultRemovalFeeExVat,
                    LineTotalExVat = _appState.Settings.DefaultRemovalFeeExVat,
                    IsVatExempt = false,
                    Description = "Auto-added removal fee"
                }
            },
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
    private void GoExport() => NavIndex = 10;

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
