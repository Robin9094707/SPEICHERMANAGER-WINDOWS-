using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Simplex.Core;
using Simplex.Core.Games;
using Simplex.Core.Scanning;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

public sealed partial class GamesPage : Page
{
    private bool _loading;
    private bool _loaded;

    public GamesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (!_loaded) await LoadAsync(); };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        _loaded = true;
        Busy.IsActive = true;
        SummaryText.Text = "Suche Spiele…";

        var games = await Task.Run(GameDetector.Detect);
        var items = games.Select(g => new GameItem(g)).ToList();
        Bind(items);

        // Unbekannte Größen parallel nachmessen
        var unknown = items.Where(i => !i.Game.SizeKnown).ToList();
        if (unknown.Count > 0)
        {
            var dq = DispatcherQueue;
            await Task.Run(() => Parallel.ForEach(unknown, new ParallelOptions { MaxDegreeOfParallelism = 4 }, item =>
            {
                long size = DirectorySize.Compute(item.InstallPath);
                dq.TryEnqueue(() => item.UpdateSize(size, 0));
            }));
            await Task.Delay(50);
            Bind(items);
        }

        Busy.IsActive = false;
        _loading = false;
    }

    private void Bind(List<GameItem> items)
    {
        var sorted = items.OrderByDescending(i => i.Size).ToList();
        long max = sorted.Count > 0 ? sorted[0].Size : 0;
        foreach (var i in sorted) i.SetMax(max);
        GameList.ItemsSource = sorted;
        EmptyText.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = sorted.Count == 0
            ? "Keine Spiele gefunden"
            : $"{sorted.Count} Spiele · zusammen {Fmt.Bytes(sorted.Sum(i => i.Size))}";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameItem g }) Elevation.ShowInExplorer(g.InstallPath);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameItem g }) return;
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"„{g.Name}“ ({g.SizeText}) wird über {g.Launcher} deinstalliert. Spielstände in der Cloud bleiben in der Regel erhalten.",
        };
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Spiel deinstallieren?", text, "Deinstallieren")) return;

        if (g.Game.UninstallUri != null)
            Elevation.ShellOpen(g.Game.UninstallUri);
        else if (!string.IsNullOrEmpty(g.Game.UninstallCommand))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + g.Game.UninstallCommand + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                Log.Write("Deinstallation: " + ex.Message);
            }
        }
        Info.Severity = InfoBarSeverity.Informational;
        Info.Title = "Deinstallation gestartet";
        Info.Message = $"Folgen Sie den Anweisungen von {g.Launcher}. Klicken Sie danach auf „Neu einlesen“.";
        Info.IsOpen = true;
    }
}
