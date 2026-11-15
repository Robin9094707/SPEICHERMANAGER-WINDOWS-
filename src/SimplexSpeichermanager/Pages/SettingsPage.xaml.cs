using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Simplex.Core;
using Simplex.Services;

namespace Simplex.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        var s = AppSettings.Current;
        ThemeRadio.SelectedIndex = Math.Clamp(s.Theme, 0, 2);
        RecycleToggle.IsOn = s.RecycleByDefault;
        AdminText.Text = Elevation.IsAdmin
            ? "Die App läuft mit Administratorrechten. Alle Bereinigungen und Systemordner sind verfügbar."
            : "Die App läuft mit Standardrechten. Für Windows-Update-Cache, Systemprotokolle und vollständige Laufwerks-Scans mit Adminrechten neu starten.";
        AdminButton.Visibility = Elevation.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        var v = typeof(SettingsPage).Assembly.GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3)} · .NET {Environment.Version.ToString(2)} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
        _ready = true;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.Theme = ThemeRadio.SelectedIndex;
        AppSettings.Current.Save();
        App.Window?.ApplyTheme(ThemeRadio.SelectedIndex);
    }

    private void Recycle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.RecycleByDefault = RecycleToggle.IsOn;
        AppSettings.Current.Save();
    }

    private void Admin_Click(object sender, RoutedEventArgs e) => AdminRestart.Restart();

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Folder);
        Elevation.ShowInExplorer(Log.Folder);
    }
}
