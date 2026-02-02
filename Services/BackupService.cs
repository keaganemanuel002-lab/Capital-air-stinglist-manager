using System;
using System.IO;
using System.IO.Compression;

namespace StingListManager.Services;

public class BackupService
{
    public string CreateBackup(string actor)
    {
        Paths.Ensure();

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(Paths.BackupsDir, $"backup_{stamp}_{Sanitize(actor)}.zip");

        if (!File.Exists(Paths.DbPath))
            throw new FileNotFoundException("Database not found.", Paths.DbPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Paths.DbPath, "sting.db");

            if (File.Exists(Paths.SettingsPath))
                zip.CreateEntryFromFile(Paths.SettingsPath, "settings.json");

            if (Directory.Exists(Paths.AttachmentsDir))
                AddDirectory(zip, Paths.AttachmentsDir, "attachments");
        }

        return zipPath;
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