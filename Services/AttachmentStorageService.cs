using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class AttachmentStorageService
{
    private static string EnsureTargetFolder(AttachmentOwnerType ownerType, int ownerId, AttachmentKind kind)
    {
        Paths.Ensure();

        // Generated quote PDFs are centralized in the managed Generated\Quotes folder.
        if (kind == AttachmentKind.QuotePdf)
            return Paths.GeneratedQuotesDir;

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
        string? notes = null,
        string? preferredFileName = null)
    {
        var folder = EnsureTargetFolder(ownerType, ownerId, kind);

        var sourceName = Path.GetFileName(sourceFilePath);
        var requestedName = string.IsNullOrWhiteSpace(preferredFileName) ? sourceName : preferredFileName;
        var safeName = SanitizeFileName(Path.GetFileName(requestedName));
        var destPath = BuildUniqueFilePath(folder, safeName);

        File.Copy(sourceFilePath, destPath, overwrite: false);

        using var db = new AppDbContext();
        var att = new Attachment
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            Kind = kind,
            FileName = Path.GetFileName(destPath),
            StoredPath = destPath,
            Notes = notes,
            AddedBy = actor
        };

        db.Attachments.Add(att);
        db.SaveChanges();
        return att;
    }

    public static string BuildUniqueFilePath(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, fileName);
        var index = 2;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{baseName}_{index}{extension}");
            index++;
        }

        return candidate;
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = fileName;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(safe) ? "attachment.pdf" : safe;
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
