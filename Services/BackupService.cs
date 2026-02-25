using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace StingListManager.Services;

public class BackupService
{
    public string CreateBackup(string actor)
    {
        Paths.Ensure();
        return CreateBackupInternal(Paths.BackupsDir, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}_{Sanitize(actor)}.zip");
    }

    public string CreateDocumentsBackup(string actor)
    {
        Paths.Ensure();
        Paths.EnsureDocumentsBackups();

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var targetFileName = $"mongo_sync_backup_{stamp}_{Sanitize(actor)}.zip";
        var zipPath = CreateBackupInternal(Paths.DocumentsBackupsDir, targetFileName);

        var latestPath = Path.Combine(Paths.DocumentsBackupsDir, "mongo_sync_latest.zip");
        File.Copy(zipPath, latestPath, overwrite: true);

        PruneOldDocumentsBackups(maxBackups: 30);
        return zipPath;
    }

    private static string CreateBackupInternal(string targetDirectory, string fileName)
    {
        Directory.CreateDirectory(targetDirectory);
        var zipPath = Path.Combine(targetDirectory, fileName);

        if (!File.Exists(Paths.DbPath))
            throw new FileNotFoundException("Database not found.", Paths.DbPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Paths.DbPath, "sting.db");
            TryAddFile(zip, $"{Paths.DbPath}-wal", "sting.db-wal");
            TryAddFile(zip, $"{Paths.DbPath}-shm", "sting.db-shm");

            if (File.Exists(Paths.SettingsPath))
                zip.CreateEntryFromFile(Paths.SettingsPath, "settings.json");

            if (Directory.Exists(Paths.AttachmentsDir))
                AddDirectory(zip, Paths.AttachmentsDir, "attachments");
        }

        return zipPath;
    }

    private static void TryAddFile(ZipArchive zip, string sourcePath, string entryName)
    {
        if (File.Exists(sourcePath))
            zip.CreateEntryFromFile(sourcePath, entryName);
    }

    private static void PruneOldDocumentsBackups(int maxBackups)
    {
        if (maxBackups < 1 || !Directory.Exists(Paths.DocumentsBackupsDir))
            return;

        var backups = Directory.GetFiles(Paths.DocumentsBackupsDir, "mongo_sync_backup_*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(x => x.CreationTimeUtc)
            .ToList();

        foreach (var stale in backups.Skip(maxBackups))
        {
            try
            {
                stale.Delete();
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    public void RestoreBackup(string zipPath)
    {
        Paths.Ensure();

        var temp = Path.Combine(Paths.BaseDir, "restore_tmp");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);

        ZipFile.ExtractToDirectory(zipPath, temp);

        var dbSource = Path.Combine(temp, "sting.db");
        if (!File.Exists(dbSource))
            throw new Exception("Backup zip is missing sting.db");

        File.Copy(dbSource, Paths.DbPath, overwrite: true);

        var walSource = Path.Combine(temp, "sting.db-wal");
        var walTarget = $"{Paths.DbPath}-wal";
        if (File.Exists(walSource))
            File.Copy(walSource, walTarget, overwrite: true);
        else if (File.Exists(walTarget))
            File.Delete(walTarget);

        var shmSource = Path.Combine(temp, "sting.db-shm");
        var shmTarget = $"{Paths.DbPath}-shm";
        if (File.Exists(shmSource))
            File.Copy(shmSource, shmTarget, overwrite: true);
        else if (File.Exists(shmTarget))
            File.Delete(shmTarget);

        var settingsSource = Path.Combine(temp, "settings.json");
        if (File.Exists(settingsSource))
            File.Copy(settingsSource, Paths.SettingsPath, overwrite: true);

        var attachmentsSource = Path.Combine(temp, "attachments");
        if (Directory.Exists(attachmentsSource))
        {
            if (Directory.Exists(Paths.AttachmentsDir))
                Directory.Delete(Paths.AttachmentsDir, true);

            CopyDir(attachmentsSource, Paths.AttachmentsDir);
        }

        Directory.Delete(temp, true);
    }

    private static void AddDirectory(ZipArchive zip, string sourceDir, string entryRoot)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var entry = Path.Combine(entryRoot, rel).Replace("\\", "/");
            zip.CreateEntryFromFile(file, entry);
        }
    }

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "user" : s;
    }
}
