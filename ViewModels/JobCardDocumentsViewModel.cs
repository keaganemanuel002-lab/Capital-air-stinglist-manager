using System;
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

public partial class AttachmentRow : ObservableObject
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string FileName { get; set; } = "";
    public string AddedAt { get; set; } = "";
    public string AddedBy { get; set; } = "";
    public string StoredPath { get; set; } = "";
}

public partial class JobCardDocumentsViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly AppState _appState;
    private readonly int _jobCardId;

    public ObservableCollection<AttachmentRow> Rows { get; } = new();

    [ObservableProperty] private AttachmentRow? selected;
    [ObservableProperty] private int kindIndex; // dropdown index

    [ObservableProperty] private string requirementStatus = "";

    public JobCardDocumentsViewModel(Window window, AppState appState, int jobCardId)
    {
        _window = window;
        _appState = appState;
        _jobCardId = jobCardId;

        Load();
        RefreshRequirements();
    }

    [RelayCommand]
    private void Load()
    {
        using var db = new AppDbContext();
        var items = db.Attachments
            .Where(a => a.OwnerType == AttachmentOwnerType.JobCard && a.OwnerId == _jobCardId)
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

    private void RefreshRequirements()
    {
        var rules = new DocumentRules();
        var ok = rules.HasRequiredDocsForJobCompletion(_jobCardId, out var msg);
        RequirementStatus = ok ? "Required documents: OK ✓" : $"⚠ {msg}";
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
            0 => AttachmentKind.QuoteSigned,
            1 => AttachmentKind.Invoice,
            2 => AttachmentKind.JobPhoto,
            _ => AttachmentKind.Other
        };

        new AttachmentStorageService().AddAttachment(
            _appState.OperatorName,
            AttachmentOwnerType.JobCard,
            _jobCardId,
            kind,
            file.Path.LocalPath);

        Load();
        RefreshRequirements();
        _appState.SetStatus("Attachment uploaded.");
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
        RefreshRequirements();
        _appState.SetStatus("Attachment deleted.");
    }
}
