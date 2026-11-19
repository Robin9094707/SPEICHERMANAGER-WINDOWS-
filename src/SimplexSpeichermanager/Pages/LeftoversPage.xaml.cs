using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Win32;
using Simplex.Core;
using Simplex.Core.Uninstall;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

/// <summary>Vorschau und Entfernen von Programmresten – nach einer Deinstallation oder als Altlasten-Suche.</summary>
public sealed partial class LeftoversPage : Page
{
    private List<LeftoverVm> _items = new();
    private List<LeftoverVm> _visible = new();
    private bool _busy;
    private bool _ready;
    private bool _hasResult;

    private static string BackupFolder => Path.Combine(Log.Folder, "Sicherungen");

    public LeftoversPage()
    {
        InitializeComponent();
        FilterCombo.SelectedIndex = 0;
        _ready = true;
        AppState.PendingLeftoversChanged += (_, _) => DispatcherQueue.TryEnqueue(TakePending);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        TakePending();
    }

    private void TakePending()
    {
        var s = AppState.TakePendingLeftovers();
        if (s != null) Show(s);
    }

    // ------------------------------------------------------------------ Anzeige

    private void Show(LeftoverSession session)
    {
        bool admin = Elevation.IsAdmin;
        _items = session.Items.Select(i => new LeftoverVm(i, admin)).ToList();
        foreach (var i in _items) i.CheckedChanged += (_, _) => UpdateTotal();
        _hasResult = true;

        TitleText.Text = session.IsOrphanScan ? "Reste-Finder – Altlasten" : session.Title;
        SubtitleText.Text = session.IsOrphanScan
            ? "Diese Einträge gehören zu keinem installierten Programm mehr. „Sicher“ und „Wahrscheinlich“ sind vorausgewählt – „Prüfen“ bitte selbst ansehen."
            : "Der Deinstaller ist fertig. Diese Reste hat er zurückgelassen – prüfen Sie die Auswahl und löschen Sie mit einem Klick.";

        IntroCard.Visibility = Visibility.Collapsed;
        ListCard.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Visible;
        Info.IsOpen = false;

        int adminCount = _items.Count(i => !i.CanRun);
        AdminInfo.Message = $"{adminCount} gefundene Reste liegen in geschützten Bereichen (Programme, ProgramData, HKLM, Dienste) und lassen sich nur mit Adminrechten entfernen.";
        AdminInfo.IsOpen = adminCount > 0;

        App.Window?.SetLeftoverBadge(_items.Count(i => i.IsChecked));
        Apply();
        UpdateTotal();
    }

    private void Apply()
    {
        if (!_ready) return;
        IEnumerable<LeftoverVm> q = _items;
        string s = SearchBox.Text?.Trim() ?? "";
        if (s.Length > 0) q = q.Where(i => i.Matches(s));
        q = FilterCombo.SelectedIndex switch
        {
            1 => q.Where(i => i.Item.Confidence == Confidence.High),
            2 => q.Where(i => i.Item.IsFileSystem),
            3 => q.Where(i => i.Item.IsRegistry),
            4 => q.Where(i => i.Item.Confidence == Confidence.Low),
            _ => q,
        };
        var visible = q.ToList();
        _visible = visible;
        var groups = visible.GroupBy(i => (i.GroupOrder, i.Group)).OrderBy(g => g.Key.GroupOrder)
            .Select(g => new LeftoverGroup(g.Key.Group, g)).ToList();
        var cvs = new Microsoft.UI.Xaml.Data.CollectionViewSource { IsSourceGrouped = true, Source = groups };
        ResultList.ItemsSource = cvs.View;
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"{_items.Count} Funde";
    }

    private IEnumerable<LeftoverVm> Visible() => _visible;

    private void UpdateTotal()
    {
        var sel = _items.Where(i => i.IsChecked).ToList();
        long size = sel.Where(i => i.Item.IsFileSystem).Sum(i => i.Item.Size);
        TotalText.Text = _hasResult ? Fmt.Bytes(size) : "–";
        if (!_hasResult)
            TotalHint.Text = "Noch keine Suche durchgeführt.";
        else if (_items.Count == 0)
            TotalHint.Text = "Keine Reste gefunden.";
        else
        {
            int files = sel.Count(i => i.Item.IsFileSystem);
            int reg = sel.Count(i => i.Item.IsRegistry);
            int other = sel.Count - files - reg;
            TotalHint.Text = $"{sel.Count} von {_items.Count} ausgewählt · {files} Dateien/Ordner · {reg} Registry" + (other > 0 ? $" · {other} Dienste/Aufgaben" : "");
        }
        DeleteButton.IsEnabled = !_busy && sel.Count > 0;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Apply();
    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) => Apply();

    private void SelectRecommended_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsChecked = i.Item.Confidence != Confidence.Low; }
    private void SelectHigh_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsChecked = i.Item.Confidence == Confidence.High; }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var i in Visible()) i.IsChecked = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsChecked = false; }

    // ------------------------------------------------------------------ Altlasten-Suche

    private async void NewScan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        IntroCard.Visibility = Visibility.Visible;
        ListCard.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        ScanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        Info.IsOpen = false;
        UpdateTotal();
        try
        {
            var progress = new Progress<string>(t => ScanText.Text = t);
            var items = await Task.Run(() => OrphanScanner.Scan(progress));
            Show(new LeftoverSession { Title = "Altlasten", Items = items, IsOrphanScan = true });
        }
        catch (Exception ex)
        {
            Log.Write("Altlasten-Suche: " + ex);
            ShowInfo(InfoBarSeverity.Error, "Suche fehlgeschlagen", ex.Message);
        }
        finally
        {
            _busy = false;
            ScanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            UpdateTotal();
        }
    }

    // ------------------------------------------------------------------ Löschen

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var sel = _items.Where(i => i.IsChecked && i.CanRun).Select(i => i.Item).ToList();
        if (sel.Count == 0) return;

        long size = sel.Where(i => i.IsFileSystem).Sum(i => i.Size);
        int low = sel.Count(i => i.Confidence == Confidence.Low);
        string? warning = low > 0
            ? $"{low} ausgewählte Einträge sind als „Prüfen“ markiert. Stellen Sie sicher, dass sie wirklich zu keinem genutzten Programm gehören."
            : null;
        var recycle = await Dialogs.ConfirmDeleteAsync(XamlRoot, "Reste entfernen?",
            $"{sel.Count} Reste werden entfernt ({Fmt.Bytes(size)} an Dateien). Die betroffene Registry wird vorher gesichert. " +
            "Programme, die noch aus diesen Ordnern laufen, werden beendet.", warning);
        if (recycle == null) return;

        _busy = true;
        UpdateTotal();
        DeleteProgress.Visibility = Visibility.Visible;
        DeleteBar.Maximum = sel.Count;
        DeleteBar.Value = 0;
        try
        {
            var progress = new Progress<(int Done, int Total, string Text)>(p =>
            {
                DeleteBar.Value = p.Done;
                DeleteText.Text = p.Text;
            });
            var result = await Task.Run(() => LeftoverCleaner.Clean(sel, recycle.Value, BackupFolder, progress));
            AppState.AddFreed(result.FreedBytes);
            foreach (var err in result.Errors.Take(30)) Log.Write("Reste: " + err);

            var removed = result.RemovedItems.Select(r => r.Key).ToHashSet();
            _items.RemoveAll(i => removed.Contains(i.Item.Key));
            Apply();
            App.Window?.SetLeftoverBadge(_items.Count(i => i.IsChecked));

            string msg = $"{result.Removed} Reste entfernt, {Fmt.Bytes(result.FreedBytes)} freigegeben." +
                         (result.Failed > 0 ? $" {result.Failed} konnten nicht entfernt werden (in Benutzung oder Adminrechte nötig)." : "") +
                         (result.Skipped > 0 ? $" {result.Skipped} geschützte Einträge übersprungen." : "") +
                         (result.BackupFile != null ? " Registry-Sicherung angelegt." : "");
            ShowInfo(result.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "Bereinigt", msg);
            if (result.Failed > 0 && result.Errors.Count > 0) Info.Message += "\n" + string.Join("\n", result.Errors.Take(4));
        }
        catch (Exception ex)
        {
            Log.Write("Reste löschen: " + ex);
            ShowInfo(InfoBarSeverity.Error, "Fehler beim Entfernen", ex.Message);
        }
        finally
        {
            _busy = false;
            DeleteProgress.Visibility = Visibility.Collapsed;
            UpdateTotal();
        }
    }

    // ------------------------------------------------------------------ Anzeigen im System

    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LeftoverVm vm }) return;
        var i = vm.Item;
        switch (i.Kind)
        {
            case LeftoverKind.Folder:
            case LeftoverKind.File:
            case LeftoverKind.Shortcut:
                Elevation.ShowInExplorer(i.Path);
                break;
            case LeftoverKind.RegistryKey:
            case LeftoverKind.RegistryValue:
            case LeftoverKind.UninstallEntry:
            case LeftoverKind.FirewallRule:
                OpenRegedit(i.Path);
                break;
            case LeftoverKind.Service:
                Elevation.ShellOpen("services.msc");
                break;
            case LeftoverKind.ScheduledTask:
                Elevation.ShellOpen("taskschd.msc");
                break;
        }
    }

    private static void OpenRegedit(string key)
    {
        try
        {
            string full = key.StartsWith("HKLM", StringComparison.Ordinal) ? "HKEY_LOCAL_MACHINE" + key[4..]
                        : key.StartsWith("HKCU", StringComparison.Ordinal) ? "HKEY_CURRENT_USER" + key[4..] : key;
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            k.SetValue("LastKey", @"Computer\" + full);
        }
        catch { }
        Elevation.ShellOpen("regedit.exe", "/m");
    }

    private void Backups_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(BackupFolder); } catch { }
        Elevation.ShowInExplorer(BackupFolder);
    }

    private void Admin_Click(object sender, RoutedEventArgs e) => AdminRestart.Restart();

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        Info.Severity = severity;
        Info.Title = title;
        Info.Message = message;
        Info.IsOpen = true;
    }
}
