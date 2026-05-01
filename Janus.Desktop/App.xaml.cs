using System;
using System.IO;
using Microsoft.UI.Xaml;
using Janus.Desktop.Services;

namespace Janus.Desktop;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static AppServices Services { get; } = new AppServices();

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Janus", "crash.log");

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var line = $"[{DateTime.Now:O}] [{source}] {ex}\n";
            File.AppendAllText(CrashLogPath, line);
        }
        catch { /* best effort */ }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        e.Handled = true; // keep app alive so user can navigate away
    }
    private void OnDomainUnhandled(object sender, System.UnhandledExceptionEventArgs e) =>
        LogCrash("Domain", e.ExceptionObject as Exception);
    private void OnUnobservedTask(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Task", e.Exception);
        e.SetObserved();
    }
}
