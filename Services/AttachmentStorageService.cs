using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class AttachmentStorageService
{
    public string EnsureOwnerFolder(AttachmentOwnerType ownerType, int ownerId)
    {
        Paths.Ensure();
        var owner = ownerType == AttachmentOwnerType.Quote ? "quote" : "jobcard";
        var path = Path.Combine(Paths.AttachmentsDir, owner, ownerId.ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    public Attachment AddAttachment(
        string actor,
        AttachmentOwnerType ownerType,
        int ownerId,
        AttachmentKind kind,
        string sourceFilePath,
        string? notes = null)
    {
        var folder = EnsureOwnerFolder(ownerType, ownerId);

        // avoid overwriting: prefix with timestamp
        var safeName = Path.GetFileName(sourceFilePath);
        var storedName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeName}";
        var destPath = Path.Combine(folder, storedName);

        File.Copy(sourceFilePath, destPath, overwrite: false);

        using var db = new AppDbContext();
        var att = new Attachment
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            Kind = kind,
            FileName = safeName,
            StoredPath = destPath,
            Notes = notes,
            AddedBy = actor
        };

        db.Attachments.Add(att);
        db.SaveChanges();
        return att;
    }

    public void OpenAttachment(string storedPath)
    {
        if (!File.Exists(storedPath))
            return;

        Process.Start(new ProcessStartInfo(storedPath) { UseShellExecute = true });
    }

    public void DeleteAttachment(int attachmentId)
    {
        using var db = new AppDbContext();
        var att = db.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (att is null) return;

        // Remove file (best effort)
        try
        {
            if (File.Exists(att.StoredPath))
                File.Delete(att.StoredPath);
        }
        catch
        {
            // if Windows is holding the file open, we still remove DB record
        }

        db.Attachments.Remove(att);
        db.SaveChanges();
    }
}
