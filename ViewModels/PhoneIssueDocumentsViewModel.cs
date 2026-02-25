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

public partial class PhoneIssueDocumentsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly int _phoneIssueId;

    public ObservableCollection<AttachmentRow> Rows { get; } = new();

    [ObservableProperty] private AttachmentRow? selected;

    public PhoneIssueDocumentsViewModel(Window window, AppState appState, int phoneIssueId)
    {
        _window = window;
        _appState = appState;
        _phoneIssueId = phoneIssueId;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var items = db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.PhoneIssue
                        && a.OwnerId == _phoneIssueId
                        && a.Kind == AttachmentKind.Invoice)
            .OrderByDescending(a => a.AddedAt)
            .ToList();

        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(new AttachmentRow
            {
                Id = item.Id,
                Kind = item.Kind.ToString(),
                FileName = item.FileName,
                AddedAt = item.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                AddedBy = item.AddedBy,
                StoredPath = item.StoredPath
            });
        }
    }

    [RelayCommand]
    private async Task Upload()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select invoice",
            AllowMultiple = false
        });

        var file = files?.FirstOrDefault();
        if (file is null)
            return;

        new AttachmentStorageService().AddAttachment(
            _appState.OperatorName,
            AttachmentOwnerType.PhoneIssue,
            _phoneIssueId,
            AttachmentKind.Invoice,
            file.Path.LocalPath);

        Load();
        _appState.SetStatus("Phone issue invoice uploaded.");
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is null)
            return;

        new AttachmentStorageService().OpenAttachment(Selected.StoredPath);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is null)
            return;

        new AttachmentStorageService().DeleteAttachment(Selected.Id);
        Load();
        _appState.SetStatus("Phone issue invoice deleted.");
    }
}
