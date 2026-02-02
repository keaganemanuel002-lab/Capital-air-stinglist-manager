using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using System.IO;
using Microsoft.EntityFrameworkCore;
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
            try
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();

                Paths.Ensure();
                var baseDir = Paths.BaseDir;

                if (!_instanceLock.TryLock(baseDir))
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

                    var lockWindow = new Window
                    {
                        Title = "StingListManager",
                        Width = 520,
                        Height = 180,
                        CanResize = false,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Content = panel
                    };

                    desktop.MainWindow = lockWindow;
                    base.OnFrameworkInitializationCompleted();
                    return;
                }

                using (var db = new AppDbContext())
                {
                    AppDbContext.ConfigureSqlitePragmas(db);
                    db.Database.Migrate();
                }

                RunAutoBackupIfDue();

                desktop.MainWindow = new MainWindow();
            }
            catch (Exception ex)
            {
                LogStartupError(ex);

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

                var errorWindow = new Window
                {
                    Title = "StingListManager",
                    Width = 640,
                    Height = 220,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = panel
                };

                desktop.MainWindow = errorWindow;
            }
        }

        base.OnFrameworkInitializationCompleted();
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