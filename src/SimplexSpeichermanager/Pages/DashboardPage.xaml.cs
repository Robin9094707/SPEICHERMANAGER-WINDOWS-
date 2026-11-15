using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Simplex.Core;
using Simplex.Core.Scanning;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

public sealed partial class DashboardPage : Page
{
    /// <summary>Wird vom Dashboard gesetzt, damit die Scan-Seite direkt ein Laufwerk scannt.</summary>
    public static string? PendingDriveScan { get; set; }

    public DashboardPage()
    {
        InitializeComponent();
        AppState.FreedChanged += OnFreedChanged;
        Unloaded += (_, _) => AppState.FreedChanged -= OnFreedChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Refresh();
    }

    private void OnFreedChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => FreedText.Text = Fmt.Bytes(AppState.FreedThisSession));

    private void Refresh()
    {
        var drives = DriveItem.Load();
        DriveList.ItemsSource = drives;
        long free = drives.Sum(d => d.Free), total = drives.Sum(d => d.Total);
        FreeTotalText.Text = Fmt.Bytes(free);
        CapacityText.Text = $"von {Fmt.Bytes(total)} auf {drives.Count} Laufwerk{(drives.Count == 1 ? "" : "en")}";
        FreedText.Text = Fmt.Bytes(AppState.FreedThisSession);
        CoresText.Text = $"{Environment.ProcessorCount} Kerne";
        AdminText.Text = $"bis zu {ScanOptions.ThreadsFor(PerformanceProfile.Maximum)} parallele Scan-Threads · " +
                         (Elevation.IsAdmin ? "Administrator" : "Standardrechte");
        int crit = drives.Count(d => d.IsCritical);
        SubtitleText.Text = crit > 0
            ? $"{crit} Laufwerk{(crit == 1 ? " ist" : "e sind")} fast voll – Zeit aufzuräumen!"
            : "Alle Laufwerke im grünen Bereich.";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) App.Window?.NavigateTo(tag);
    }

    private void ScanDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string root })
        {
            PendingDriveScan = root;
            App.Window?.NavigateTo("scan");
        }
    }
}
