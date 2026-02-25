using System;
using System.IO;

namespace StingListManager.Services;

public static class Paths
{
    public static string LocalBaseDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StingListManager");

    public static string UserDocumentsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "StingListManager");

    public static string SettingsPath => Path.Combine(LocalBaseDir, "settings.json");
    public static string StartupLogPath => Path.Combine(LocalBaseDir, "startup.log");

    public static string BaseDir
    {
        get
        {
            var settings = new SettingsService().Load();
            if (settings.UseSharedData && !string.IsNullOrWhiteSpace(settings.SharedBaseDir))
                return settings.SharedBaseDir!;

            return LocalBaseDir;
        }
    }

    public static string DbPath => Path.Combine(BaseDir, "sting.db");
    public static string OrdersDbPath => Path.Combine(BaseDir, "orders.db");
    public static string AttachmentsDir => Path.Combine(BaseDir, "attachments");
    public static string GeneratedDir => Path.Combine(BaseDir, "generated");
    public static string GeneratedQuotesDir => Path.Combine(GeneratedDir, "quotes");
    public static string GeneratedJobCardsDir => Path.Combine(GeneratedDir, "jobcards");
    public static string BackupsDir => Path.Combine(BaseDir, "backups");
    public static string DocumentsBackupsDir => Path.Combine(UserDocumentsDir, "mongo-sync-backups");
    public static string ProductCatalogPath => Path.Combine(BaseDir, "products.json");

    public static void EnsureLocal()
    {
        Directory.CreateDirectory(LocalBaseDir);
    }

    public static void Ensure()
    {
        EnsureLocal();
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(GeneratedDir);
        Directory.CreateDirectory(GeneratedQuotesDir);
        Directory.CreateDirectory(GeneratedJobCardsDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public static void EnsureDocumentsBackups()
    {
        Directory.CreateDirectory(UserDocumentsDir);
        Directory.CreateDirectory(DocumentsBackupsDir);
    }
}
