using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StingListManager.Data;
using StingListManager.Data.Entities;
using StingListManager.Services;

namespace StingListManager.ViewModels;

public partial class QuoteDocumentsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly int _quoteId;

    public ObservableCollection<AttachmentRow> Rows { get; } = new();

    [ObservableProperty] private AttachmentRow? selected;
    [ObservableProperty] private int kindIndex; // 0 Quote PDF, 1 Signed Quote, 2 Invoice, 3 Other

    public QuoteDocumentsViewModel(Window window, AppState appState, int quoteId)
    {
        _window = window;
        _appState = appState;
        _quoteId = quoteId;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var items = db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.Quote && a.OwnerId == _quoteId)
            .OrderByDescending(a => a.AddedAt)
            .ToList();

        Rows.Clear();
        foreach (var a in items)
        {
            Rows.Add(new AttachmentRow
            {
                Id = a.Id,
                Kind = a.Kind.ToString(),
                FileName = a.FileName,
                AddedAt = a.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                AddedBy = a.AddedBy,
                StoredPath = a.StoredPath
            });
        }
    }

    [RelayCommand]
    private async Task Upload()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select attachment",
            AllowMultiple = false
        });

        var file = files?.FirstOrDefault();
        if (file is null) return;

        var kind = KindIndex switch
        {
            0 => AttachmentKind.QuotePdf,
            1 => AttachmentKind.QuoteSigned,
            2 => AttachmentKind.Invoice,
            _ => AttachmentKind.Other
        };

        new AttachmentStorageService().AddAttachment(
            _appState.OperatorName,
            AttachmentOwnerType.Quote,
            _quoteId,
            kind,
            file.Path.LocalPath);

        Load();
        _appState.SetStatus("Quote attachment uploaded.");
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is null) return;
        new AttachmentStorageService().OpenAttachment(Selected.StoredPath);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is null) return;
        new AttachmentStorageService().DeleteAttachment(Selected.Id);
        Load();
        _appState.SetStatus("Quote attachment deleted.");
    }
}
