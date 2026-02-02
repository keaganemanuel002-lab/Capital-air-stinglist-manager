using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class ExportViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;

    [ObservableProperty] private int year;
    [ObservableProperty] private int month;

    public ExportViewModel(Window window, AppState appState)
    {
        _window = window;
        _appState = appState;

        var now = DateTime.Now;
        Year = now.Year;
        Month = now.Month;
    }

    public bool CanExport => _appState.CanExport;

    [RelayCommand]
    private async Task ExportMonthly()
    {
        if (!CanExport) { _appState.SetStatus("Not permitted."); return; }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Monthly Export",
            SuggestedFileName = $"STING Export {Year:D4}-{Month:D2}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel file") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file is null) return;

        var path = file.Path.LocalPath;
        var exporter = new ExcelExportService();
        exporter.ExportMonthly(path, Year, Month);

        _appState.SetStatus($"Export saved: {Path.GetFileName(path)}");

        // Show completion notification
        await DialogService.Confirm(
            _window,
            "Export Complete",
            $"File saved to:\n\n{Path.GetFileName(path)}\n\nReady to send to billing or accounting."
        );
    }

    [RelayCommand]
    private async Task ExportCancellations()
    {
        if (!CanExport) { _appState.SetStatus("Not permitted."); return; }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Cancellations Export",
            SuggestedFileName = $"Cancellations {Year:D4}-{Month:D2}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel file") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file is null) return;

        var path = file.Path.LocalPath;
        var exporter = new ExcelExportService();
        exporter.ExportCancellationsOnly(path, Year, Month);

        _appState.SetStatus($"Cancellations export saved: {Path.GetFileName(path)}");
    }
}
