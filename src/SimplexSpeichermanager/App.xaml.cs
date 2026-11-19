using Microsoft.UI.Xaml;
using Simplex.Services;

namespace Simplex;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Write("Unbehandelte Ausnahme: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("Task: " + e.Exception); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            _ = SelfTestAsync();
    }

    /// <summary>CI-Selbsttest: öffnet jede Seite einmal und beendet die App. Fehler landen im Log.</summary>
    private static async Task SelfTestAsync()
    {
        Log.Write("SELFTEST START");
        foreach (var tag in new[] { "dashboard", "scan", "folders", "cleanup", "games", "programs", "leftovers", "settings", "dashboard" })
        {
            try
            {
                Window!.NavigateTo(tag);
                await Task.Delay(2500);
                Log.Write("SELFTEST Seite ok: " + tag);
            }
            catch (Exception ex)
            {
                Log.Write("SELFTEST FEHLER " + tag + ": " + ex);
            }
        }
        Log.Write("SELFTEST ENDE");
        Current.Exit();
    }
}
