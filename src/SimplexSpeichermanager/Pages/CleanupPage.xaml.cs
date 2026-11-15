using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Simplex.Core;
using Simplex.Core.Cleanup;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

public sealed partial class CleanupPage : Page
{
    private List<CleanupItem> _items = new();
    private bool _busy;
    private bool _analyzedOnce;

    public CleanupPage()
    {
        InitializeComponent();
        AdminLink.Visibility = Elevation.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        Loaded += async (_, _) =>
        {
            if (_items.Count == 0) await BuildAsync();
            if (!_analyzedOnce) await AnalyzeAsync();
        };
    }

    private async Task BuildAsync()
    {
        var cats = await Task.Run(CleanupCatalog.Build);
        bool admin = Elevation.IsAdmin;
        _items = cats.Select(c => new CleanupItem(c, admin)).ToList();
        foreach (var i in _items) i.CheckedChanged += (_, _) => UpdateTotal();
        var groups = _items.GroupBy(i => i.Category.Group).Select(g => new CleanupGroup(g.Key, g)).ToList();
        var cvs = new Microsoft.UI.Xaml.Data.CollectionViewSource { IsSourceGrouped = true, Source = groups };
        CleanupList.ItemsSource = cvs.View;

        long hiber = await Task.Run(CleanupEngine.HiberfilSize);
        if (hiber > 0)
        {
            HiberText.Text = $"Die Ruhezustandsdatei belegt {Fmt.Bytes(hiber)}. Ohne Ruhezustand startet der PC trotzdem normal (Schnellstart wird deaktiviert).";
            HiberButton.IsEnabled = true;
        }
        else
        {
            HiberText.Text = "Der Ruhezustand ist bereits deaktiviert – keine hiberfil.sys vorhanden.";
            HiberButton.IsEnabled = false;
        }
    }

    private void UpdateTotal()
    {
        var sel = _items.Where(i => i.IsChecked == true && i.CanRun).ToList();
        long total = sel.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
        TotalText.Text = _analyzedOnce ? Fmt.Bytes(total) : "–";
        TotalHint.Text = _analyzedOnce
            ? $"{sel.Count} Kategorie(n) ausgewählt · insgesamt freigebbar: {Fmt.Bytes(_items.Where(i => i.CanRun && i.Bytes > 0).Sum(i => i.Bytes))}"
            : "Analyse läuft…";
        CleanButton.IsEnabled = !_busy && sel.Count > 0;
        AnalyzeButton.IsEnabled = !_busy;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateTotal();
        foreach (var i in _items) { i.Status = null; i.IsBusy = true; }

        var dq = DispatcherQueue;
        await Task.Run(() => Parallel.ForEach(_items, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
        {
            CleanupAnalysis a;
            try { a = CleanupEngine.Analyze(item.Category); }
            catch { a = new CleanupAnalysis(); }
            dq.TryEnqueue(() =>
            {
                item.Bytes = a.Bytes;
                item.IsBusy = false;
                if (a.Bytes == 0) item.Status = "leer";
                _analyzedOnce = true;
                UpdateTotal();
            });
        }));

        _busy = false;
        _analyzedOnce = true;
        UpdateTotal();
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var sel = _items.Where(i => i.IsChecked == true && i.CanRun).ToList();
        if (sel.Count == 0) return;
        long expected = sel.Where(i => i.Bytes > 0).Sum(i => i.Bytes);

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{sel.Count} Kategorie(n) werden bereinigt" + (expected > 0 ? $" (ca. {Fmt.Bytes(expected)})" : "") +
                   ".\n\nDateien, die gerade verwendet werden, werden automatisch übersprungen. Für das beste Ergebnis Browser und Spiele vorher schließen.",
        };
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Jetzt bereinigen?", text, "Bereinigen")) return;

        _busy = true;
        UpdateTotal();
        foreach (var i in sel) { i.Status = null; i.IsBusy = true; }

        long freed = 0, failed = 0;
        var dq = DispatcherQueue;
        await Task.Run(() =>
        {
            foreach (var item in sel)
            {
                CleanupRunResult r;
                try { r = CleanupEngine.Clean(item.Category); }
                catch (Exception ex) { Log.Write("Bereinigung " + item.Category.Id + ": " + ex.Message); r = new CleanupRunResult(); }
                Interlocked.Add(ref freed, r.FreedBytes);
                Interlocked.Add(ref failed, r.FailedFiles);
                long left = 0;
                try { left = CleanupEngine.Analyze(item.Category).Bytes; } catch { }
                var res = r;
                dq.TryEnqueue(() =>
                {
                    item.IsBusy = false;
                    item.Bytes = left;
                    item.Status = res.FreedBytes > 0 ? "−" + Fmt.Bytes(res.FreedBytes) : (left == 0 ? "leer" : null);
                });
            }
        });

        AppState.AddFreed(freed);
        _busy = false;
        UpdateTotal();
        Info.Severity = InfoBarSeverity.Success;
        Info.Title = "Bereinigung abgeschlossen";
        Info.Message = $"{Fmt.Bytes(freed)} freigegeben." +
                       (failed > 0 ? $" {Fmt.Number(failed)} Datei(en) waren in Benutzung und wurden übersprungen." : "");
        Info.IsOpen = true;
    }

    private void Admin_Click(object sender, RoutedEventArgs e) => AdminRestart.Restart();

    private async void Hiber_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "Der Ruhezustand (und der Windows-Schnellstart) wird deaktiviert und hiberfil.sys gelöscht. " +
                   "Rückgängig machen jederzeit mit „powercfg /hibernate on“ in einer Admin-Konsole.",
        };
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Ruhezustand deaktivieren?", text, "Deaktivieren")) return;
        long before = CleanupEngine.HiberfilSize();
        if (!Elevation.RunElevated("powercfg.exe", "/hibernate off", showWindow: false)) return;
        await Task.Delay(2500);
        long after = await Task.Run(CleanupEngine.HiberfilSize);
        if (after == 0)
        {
            AppState.AddFreed(before);
            HiberText.Text = $"Erledigt – {Fmt.Bytes(before)} freigegeben.";
            HiberButton.IsEnabled = false;
        }
    }

    private void Dism_Click(object sender, RoutedEventArgs e) =>
        Elevation.RunElevated("cmd.exe", "/k title Simplex - WinSxS-Bereinigung && Dism.exe /Online /Cleanup-Image /StartComponentCleanup && echo. && echo Fertig! Dieses Fenster kann geschlossen werden.");

    private void Cleanmgr_Click(object sender, RoutedEventArgs e) => Elevation.ShellOpen("cleanmgr.exe");

    private void StorageSense_Click(object sender, RoutedEventArgs e) => Elevation.ShellOpen("ms-settings:storagesense");
}
