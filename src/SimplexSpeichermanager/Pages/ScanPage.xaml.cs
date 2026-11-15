using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Simplex.Core;
using Simplex.Core.Safety;
using Simplex.Core.Scanning;
using Simplex.Services;
using Simplex.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Simplex.Pages;

public sealed partial class ScanPage : Page
{
    private static readonly long[] s_minSizes =
    {
        10L << 20, 50L << 20, 100L << 20, 500L << 20, 1L << 30, 5L << 30,
    };

    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _watch = new();
    private CancellationTokenSource? _cts;
    private ScanProgress? _progress;
    private List<FileItem> _all = new();
    private bool _loaded;

    public ScanPage()
    {
        InitializeComponent();

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(120);
        _timer.Tick += (_, _) => UpdateProgress();

        int cores = Environment.ProcessorCount;
        PerfCombo.Items.Add($"Maximal – {ScanOptions.ThreadsFor(PerformanceProfile.Maximum)} Threads (SSD/NVMe)");
        PerfCombo.Items.Add($"Ausgewogen – {ScanOptions.ThreadsFor(PerformanceProfile.Balanced)} Threads");
        PerfCombo.Items.Add("Schonend – 2 Threads (HDD / Hintergrund)");

        var s = AppSettings.Current;
        MinSizeCombo.SelectedIndex = Math.Clamp(s.MinSizeIndex, 0, s_minSizes.Length - 1);
        PerfCombo.SelectedIndex = Math.Clamp(s.PerformanceIndex, 0, 2);
        HiddenCheck.IsChecked = s.IncludeHidden;
        ProtectedCheck.IsChecked = s.HideProtected;
        SortCombo.SelectedIndex = 0;

        LoadDrives();
        ModeRadio.SelectedIndex = 0;
        _loaded = true;
        UpdateModeUi();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DashboardPage.PendingDriveScan is { } root)
        {
            DashboardPage.PendingDriveScan = null;
            if (_cts != null) return;
            LoadDrives();
            ModeRadio.SelectedIndex = 1;
            var items = DriveCombo.Items.OfType<DriveItem>().ToList();
            var match = items.FirstOrDefault(d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase));
            if (match != null) DriveCombo.SelectedItem = match;
            Start_Click(this, new RoutedEventArgs());
        }
    }

    private void LoadDrives()
    {
        var selected = (DriveCombo.SelectedItem as DriveItem)?.Root;
        DriveCombo.Items.Clear();
        foreach (var d in DriveItem.Load()) DriveCombo.Items.Add(d);
        var again = DriveCombo.Items.OfType<DriveItem>().FirstOrDefault(d => d.Root == selected);
        DriveCombo.SelectedItem = again ?? DriveCombo.Items.FirstOrDefault();
    }

    private void ModeRadio_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateModeUi();

    private void UpdateModeUi()
    {
        if (!_loaded) return;
        int mode = ModeRadio.SelectedIndex;
        QuickHint.Visibility = mode is 0 or 2 ? Visibility.Visible : Visibility.Collapsed;
        DriveCombo.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        QuickHint.Text = mode == 0
            ? "Durchsucht Ihr Benutzerprofil (Downloads, Desktop, Dokumente, Videos, AppData …) – in Sekunden fertig."
            : "Durchsucht alle internen und externen Laufwerke gleichzeitig.";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = App.Window != null ? WinRT.Interop.WindowNative.GetWindowHandle(App.Window) : IntPtr.Zero;
        var path = FolderPicker.Pick(hwnd, "Ordner zum Scannen auswählen");
        if (!string.IsNullOrEmpty(path)) FolderBox.Text = path;
    }

    private List<string>? ResolveRoots()
    {
        switch (ModeRadio.SelectedIndex)
        {
            case 0:
                return new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            case 1:
                return DriveCombo.SelectedItem is DriveItem d ? new List<string> { d.Root } : null;
            case 2:
                return DriveItem.Load().Select(x => x.Root).ToList();
            case 3:
                var p = FolderBox.Text.Trim().Trim('"');
                return Directory.Exists(p) ? new List<string> { p } : null;
            default:
                return null;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) return;
        var roots = ResolveRoots();
        if (roots == null || roots.Count == 0)
        {
            ShowInfo(InfoBarSeverity.Warning, "Kein gültiges Ziel", "Bitte wählen Sie ein Laufwerk oder einen existierenden Ordner aus.");
            return;
        }

        var s = AppSettings.Current;
        s.MinSizeIndex = MinSizeCombo.SelectedIndex;
        s.PerformanceIndex = PerfCombo.SelectedIndex;
        s.IncludeHidden = HiddenCheck.IsChecked == true;
        s.HideProtected = ProtectedCheck.IsChecked == true;
        s.Save();

        var options = new ScanOptions
        {
            Roots = roots,
            MinFileSize = s_minSizes[Math.Max(0, MinSizeCombo.SelectedIndex)],
            IncludeHidden = HiddenCheck.IsChecked == true,
            Profile = (PerformanceProfile)Math.Max(0, PerfCombo.SelectedIndex),
        };

        _cts = new CancellationTokenSource();
        _progress = new ScanProgress();
        SetScanning(true);
        _watch.Restart();
        _timer.Start();

        ScanResult result;
        try
        {
            result = await FileScanner.RunAsync(options, _progress, _cts.Token);
        }
        catch (Exception ex)
        {
            Log.Write("Scan fehlgeschlagen: " + ex);
            ShowInfo(InfoBarSeverity.Error, "Scan fehlgeschlagen", ex.Message);
            _timer.Stop();
            SetScanning(false);
            _cts = null;
            return;
        }

        _timer.Stop();
        _watch.Stop();
        UpdateProgress();
        _cts = null;
        SetScanning(false);

        AppState.SetScan(result);
        await LoadResultsAsync(result);

        double rate = result.Duration.TotalSeconds > 0 ? result.TotalFiles / result.Duration.TotalSeconds : 0;
        ShowInfo(result.Cancelled ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            result.Cancelled ? "Scan abgebrochen" : "Scan abgeschlossen",
            $"{Fmt.Number(result.TotalFiles)} Dateien ({Fmt.Bytes(result.TotalBytes)}) in {Fmt.Duration(result.Duration)} " +
            $"mit {result.Threads} Threads – {Fmt.Number((long)rate)} Dateien/s. {Fmt.Number(result.LargeFiles.Count)} große Dateien gefunden." +
            (result.Errors > 0 && !Elevation.IsAdmin ? " Einige Ordner waren ohne Adminrechte nicht lesbar." : ""));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetScanning(bool scanning)
    {
        StartButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        ModeRadio.IsEnabled = !scanning;
        ProgressCard.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressBar.IsIndeterminate = scanning;
        if (scanning)
        {
            Info.IsOpen = false;
            EmptyText.Text = "Scan läuft…";
            ResultList.ItemsSource = null;
            EmptyState.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateProgress()
    {
        var p = _progress;
        if (p == null) return;
        double secs = Math.Max(0.001, _watch.Elapsed.TotalSeconds);
        StatFiles.Text = Fmt.Number(p.Files);
        StatDirs.Text = Fmt.Number(p.Directories);
        StatBytes.Text = Fmt.Bytes(p.Bytes);
        StatRate.Text = Fmt.Number((long)(p.Files / secs)) + " /s";
        StatFound.Text = Fmt.Number(p.Found);
        ElapsedText.Text = Fmt.Duration(_watch.Elapsed);
        CurrentPathText.Text = p.CurrentPath;
    }

    private async Task LoadResultsAsync(ScanResult result)
    {
        _all = await Task.Run(() => result.LargeFiles.Select(f => new FileItem(f)).ToList());

        CategoryCombo.SelectionChanged -= Filter_SelectionChanged;
        CategoryCombo.Items.Clear();
        CategoryCombo.Items.Add(new ComboBoxItem { Content = $"Alle Kategorien ({Fmt.Number(_all.Count)})", Tag = -1 });
        foreach (var grp in _all.GroupBy(f => f.Category).OrderByDescending(g => g.Sum(x => x.Size)))
        {
            CategoryCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{FileCategories.DisplayName(grp.Key)} · {Fmt.Bytes(grp.Sum(x => x.Size))}",
                Tag = (int)grp.Key,
            });
        }
        CategoryCombo.SelectedIndex = 0;
        CategoryCombo.SelectionChanged += Filter_SelectionChanged;

        Toolbar.Visibility = Visibility.Visible;
        ApplyFilter();
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private void ApplyFilter()
    {
        if (!_loaded || _all.Count == 0 && AppState.LastScan == null) return;

        IEnumerable<FileItem> q = _all;
        if (ProtectedCheck.IsChecked == true) q = q.Where(f => !f.IsProtected);
        if (CategoryCombo.SelectedItem is ComboBoxItem { Tag: int cat } && cat >= 0) q = q.Where(f => (int)f.Category == cat);
        string search = SearchBox.Text?.Trim() ?? "";
        if (search.Length > 0) q = q.Where(f => f.FullPath.Contains(search, StringComparison.OrdinalIgnoreCase));
        q = SortCombo.SelectedIndex switch
        {
            1 => q.OrderBy(f => f.Entry.LastWriteUtc),
            2 => q.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => q.OrderByDescending(f => f.Size),
        };
        var list = q.ToList();
        ResultList.ItemsSource = list;
        EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _all.Count == 0 ? "Keine Dateien über der Mindestgröße gefunden." : "Keine Treffer für diesen Filter.";
        UpdateSelectionUi();
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionUi();

    private void UpdateSelectionUi()
    {
        var sel = ResultList.SelectedItems.OfType<FileItem>().Where(f => !f.IsProtected).ToList();
        DeleteButton.IsEnabled = sel.Count > 0;
        DeleteButtonText.Text = sel.Count == 0 ? "Löschen" : $"{sel.Count} löschen ({Fmt.Bytes(sel.Sum(f => f.Size))})";
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ResultList.SelectedItems.OfType<FileItem>().FirstOrDefault();
        if (item != null) Elevation.ShowInExplorer(item.FullPath);
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) =>
        _ = DeleteAsync(ResultList.SelectedItems.OfType<FileItem>().ToList());

    private void ItemShow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FileItem f }) Elevation.ShowInExplorer(f.FullPath);
    }

    private void ItemCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FileItem f })
        {
            var pkg = new DataPackage();
            pkg.SetText(f.FullPath);
            Clipboard.SetContent(pkg);
        }
    }

    private void ItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FileItem f }) _ = DeleteAsync(new List<FileItem> { f });
    }

    private async Task DeleteAsync(List<FileItem> items)
    {
        var protectedOnes = items.Where(f => f.IsProtected).ToList();
        var deletable = items.Where(f => !f.IsProtected).ToList();
        if (deletable.Count == 0)
        {
            ShowInfo(InfoBarSeverity.Warning, "Geschützt", "Die ausgewählten Dateien sind systemwichtig und können nicht gelöscht werden.");
            return;
        }

        long total = deletable.Sum(f => f.Size);
        int caution = deletable.Count(f => f.Safety.Level == SafetyLevel.Caution);
        string msg = deletable.Count == 1
            ? $"„{deletable[0].Name}“ ({Fmt.Bytes(total)}) wird gelöscht."
            : $"{deletable.Count} Dateien mit zusammen {Fmt.Bytes(total)} werden gelöscht.";
        if (protectedOnes.Count > 0) msg += $" {protectedOnes.Count} geschützte Datei(en) werden übersprungen.";
        string? warn = caution > 0
            ? $"{caution} Datei(en) gehören zu Programmen, Spielen oder App-Daten. Das betroffene Programm funktioniert danach evtl. nicht mehr richtig."
            : null;

        bool? recycle = await Dialogs.ConfirmDeleteAsync(XamlRoot, deletable.Count == 1 ? "Datei löschen?" : "Dateien löschen?", msg, warn);
        if (recycle == null) return;

        DeleteButton.IsEnabled = false;
        var requests = deletable.Select(f => new DeleteRequest(f.FullPath, f.Size, f.Entry.Attributes)).ToList();
        var outcome = await Task.Run(() => Deleter.Delete(requests, recycle.Value));

        var deleted = new HashSet<string>(outcome.DeletedPaths, StringComparer.OrdinalIgnoreCase);
        var scan = AppState.LastScan;
        foreach (var f in deletable.Where(f => deleted.Contains(f.FullPath)))
        {
            scan?.LargeFiles.Remove(f.Entry);
            scan?.FindNode(f.Directory)?.SubtractFile(f.Size);
        }
        _all.RemoveAll(f => deleted.Contains(f.FullPath));
        ApplyFilter();

        if (!recycle.Value) AppState.AddFreed(outcome.FreedBytes);
        string where = recycle.Value ? " in den Papierkorb verschoben (leeren Sie ihn, um den Platz freizugeben)" : " endgültig gelöscht";
        if (outcome.Failed == 0)
            ShowInfo(InfoBarSeverity.Success, "Erledigt", $"{outcome.Deleted} Datei(en) mit {Fmt.Bytes(outcome.FreedBytes)}{where}.");
        else
            ShowInfo(InfoBarSeverity.Warning, "Teilweise erledigt",
                $"{outcome.Deleted} gelöscht ({Fmt.Bytes(outcome.FreedBytes)}), {outcome.Failed} fehlgeschlagen: {string.Join("; ", outcome.Errors.Take(3))}");
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        Info.Severity = severity;
        Info.Title = title;
        Info.Message = message;
        Info.IsOpen = true;
    }
}
