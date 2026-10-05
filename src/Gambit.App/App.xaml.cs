using System.Diagnostics;
using Gambit.App.Services;
using Microsoft.UI.Xaml;

namespace Gambit.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true; // keep the app alive; the error is logged
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public static SettingsService Settings { get; private set; } = null!;
    public static ProfileService Profile { get; private set; } = null!;
    public static MainWindow Window { get; private set; } = null!;

    /// <summary>
    /// Starts a new copy of the app (same data folder) and closes this one. Used after the data folder
    /// was restored or reset, so nothing left in memory writes over it.
    /// </summary>
    public static void Restart()
    {
        AppPaths.Frozen = true;
        Log.Info("Restarting");
        SingleInstance.Release();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error("Restarting failed", ex);
        }
        Current.Exit();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info($"{AppInfo.DisplayName} {AppInfo.Version} starting ({AppInfo.Architecture})");
        Settings = new SettingsService();
        Profile = new ProfileService();
        Theming.AppAccent.Apply(Helpers.BoardSettings.CurrentTheme(Settings.Current).Accent);
        Window = new MainWindow();
        if (AppPaths.IsTestProfile)
        {
            // Test runs (tools/ scripts) open behind the user's windows without taking focus, so
            // nothing the user types lands in them.
            Window.AppWindow.Show(activateWindow: false);
            Window.SendToBack();
        }
        else
        {
            Window.Activate();
        }

        // Installed copies look for a new version on the online server, quietly, once things have settled.
        Window.DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            await UpdateService.Instance.CheckAsync();
        });
    }
}
