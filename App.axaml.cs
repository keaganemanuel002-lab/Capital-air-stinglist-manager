using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.IO;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using StingListManager.Data;
using StingListManager.ViewModels;
using StingListManager.Views;
using StingListManager.Services;

namespace StingListManager;

public partial class App : Application
{
    private readonly InstanceLock _instanceLock = new();

    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            LogStartupError(ex);
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.Exit += (_, _) =>
            {
                try
                {
                    _instanceLock.Dispose();
                }
                catch
                {
                    // ignore lock release failures on exit
                }

                try
                {
                    var stopTask = Task.Run(async () =>
                    {
                        try
                        {
                            await TechnicianApiHostService.Instance.StopAsync();
                        }
                        catch
                        {
                            // ignore shutdown failures on exit
                        }

                        try
                        {
                            await FirebaseSyncService.Instance.StopAsync();
                        }
                        catch
                        {
                            // ignore shutdown failures on exit
                        }
                    });

                    _ = stopTask.Wait(TimeSpan.FromSeconds(3));
                }
                catch
                {
                    // ignore shutdown failures on exit
                }
            };

            var splash = new StartupSplashWindow();
            desktop.MainWindow = splash;
            splash.Show();
            _ = InitializeDesktopStartupAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeDesktopStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        StartupSplashWindow splash)
    {
        try
        {
            DisableAvaloniaDataAnnotationValidation();
            await Dispatcher.UIThread.InvokeAsync(() => splash.SetStatus("Preparing local data location..."));

            Paths.Ensure();
            var baseDir = Paths.BaseDir;

            await Dispatcher.UIThread.InvokeAsync(() => splash.SetStatus("Checking running instance lock..."));
            if (!_instanceLock.TryLock(baseDir))
            {
                var lockWindow = BuildLockWindow(desktop, baseDir);
                desktop.MainWindow = lockWindow;
                lockWindow.Show();
                splash.Close();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => splash.SetStatus("Applying database migrations..."));
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                AppDbContext.ConfigureSqlitePragmas(db);
                db.Database.Migrate();
                BackfillClients(db);
                AuthService.EnsureDefaultAdminUser(db);
            });

            await Dispatcher.UIThread.InvokeAsync(() => splash.SetStatus("Finalizing startup checks..."));
            await Task.Run(RunAutoBackupIfDue);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var loginWindow = BuildLoginWindow(desktop);
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
                splash.Close();
            });
        }
        catch (Exception ex)
        {
            LogStartupError(ex);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var errorWindow = BuildStartupErrorWindow(desktop, ex);
                desktop.MainWindow = errorWindow;
                errorWindow.Show();
                splash.Close();
            });
        }
    }

    private static LoginWindow BuildLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var loginWindow = new LoginWindow();
        loginWindow.Closed += (_, _) =>
        {
            if (!loginWindow.LoginSucceeded)
            {
                desktop.Shutdown();
                return;
            }

            var mainWindow = new MainWindow(
                loginWindow.AuthenticatedUsername,
                loginWindow.AuthenticatedRole);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        };

        return loginWindow;
    }

    private static Window BuildLockWindow(IClassicDesktopStyleApplicationLifetime desktop, string baseDir)
    {
        var exitButton = new Button { Content = "Exit", Width = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        exitButton.Click += (_, _) => desktop.Shutdown();

        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "App already running for this data location.", FontSize = 16 },
                new TextBlock { Text = baseDir, Opacity = 0.7 },
                exitButton
            }
        };

        return new Window
        {
            Title = "StingListManager",
            Width = 520,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };
    }

    private static Window BuildStartupErrorWindow(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        var exitButton = new Button { Content = "Exit", Width = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        exitButton.Click += (_, _) => desktop.Shutdown();

        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "Startup failed. See log file:", FontSize = 16 },
                new TextBlock { Text = Paths.StartupLogPath, Opacity = 0.7 },
                new TextBlock { Text = ex.Message, Opacity = 0.8 },
                exitButton
            }
        };

        return new Window
        {
            Title = "StingListManager",
            Width = 640,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel
        };
    }

    private static void LogStartupError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Paths.LocalBaseDir);
            var contents = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n";
            File.AppendAllText(Paths.StartupLogPath, contents);
        }
        catch
        {
            // ignore logging failures
        }
    }

    private void RunAutoBackupIfDue()
    {
        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        if (!settings.AutoBackupOnStartup)
            return;

        var today = DateTime.Today;
        if (settings.LastAutoBackupDate?.Date == today)
            return;

        try
        {
            var svc = new BackupService();
            svc.CreateBackup(settings.OperatorName);
            settings.LastAutoBackupDate = today;
            settingsService.Save(settings);
        }
        catch
        {
            // Swallow on startup; user can run manual backup later if needed
        }
    }

    private static void BackfillClients(AppDbContext db)
    {
        var existingNorms = db.Clients.AsNoTracking()
            .Select(c => c.NameNorm)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        var sourceNames = db.Quotes.AsNoTracking().Select(q => q.Company)
            .Concat(db.JobCards.AsNoTracking().Select(j => j.Company))
            .Concat(db.BillingEntries.AsNoTracking().Select(b => b.Company))
            .Where(n => n != null)
            .ToList();

        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawName in sourceNames)
        {
            var displayName = NormalizeClientDisplayName(rawName);
            if (string.IsNullOrWhiteSpace(displayName))
                continue;

            var normKey = NormalizeClientComparableName(displayName);
            if (string.IsNullOrWhiteSpace(normKey))
                continue;

            if (existingNorms.Contains(normKey) || pending.ContainsKey(normKey))
                continue;

            pending[normKey] = displayName;
        }

        foreach (var candidate in pending)
        {
            db.Clients.Add(new Data.Entities.Client
            {
                Name = candidate.Value,
                NameNorm = candidate.Key,
                CreatedAt = DateTime.UtcNow
            });
            existingNorms.Add(candidate.Key);
        }

        if (pending.Count > 0)
        {
            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("Clients.NameNorm", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Do not block startup when duplicate client norms already exist in source data.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static string NormalizeClientDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeClientComparableName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

}
