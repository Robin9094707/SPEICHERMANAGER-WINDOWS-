using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Simplex.Core;
using Simplex.Core.Scanning;
using Simplex.Core.Uninstall;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

/// <summary>Deinstallations-Modul: Programme und Store-Apps (inkl. Bloatware) restlos entfernen.</summary>
public sealed partial class ProgramsPage : Page
{
    private List<AppItem> _win32 = new();
    private List<AppItem> _store = new();
    private bool _loading;
    private bool _busy;
    private bool _ready;
    private CancellationTokenSource? _waitCts;

    public ProgramsPage()
    {
        InitializeComponent();
        var s = AppSettings.Current;
        FilterCombo.SelectedIndex = 0;
        SortCombo.SelectedIndex = 0;
        SilentToggle.IsOn = s.SilentUninstall;
        RestoreToggle.IsOn = s.RestorePoint && Elevation.IsAdmin;
        AllUsersToggle.IsOn = s.StoreAllUsers && Elevation.IsAdmin;
        RestoreToggle.IsEnabled = Elevation.IsAdmin;
        AllUsersToggle.IsEnabled = Elevation.IsAdmin;
        AdminOptionsHint.Visibility = Elevation.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        AdminInfo.IsOpen = !Elevation.IsAdmin;
        _ready = true;
        Loaded += async (_, _) => { if (_win32.Count == 0 && _store.Count == 0) await LoadAsync(); };
    }

    private bool StoreTab => Tabs.SelectedItem == TabStore;
    private IEnumerable<AppItem> AllItems => _win32.Concat(_store);

    // ------------------------------------------------------------------ Laden

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        Busy.IsActive = true;
        try
        {
            var checkedIds = AllItems.Where(i => i.IsChecked).Select(i => i.App.Id).ToHashSet();
            var win32Task = Task.Run(() => AppCatalog.LoadWin32());
            var storeTask = Task.Run(AppCatalog.LoadStore);
            await Task.WhenAll(win32Task, storeTask);
            _win32 = win32Task.Result.Select(a => new AppItem(a)).ToList();
            _store = storeTask.Result.Select(a => new AppItem(a)).ToList();
            foreach (var i in AllItems)
            {
                if (checkedIds.Contains(i.App.Id)) i.IsChecked = true;
                i.CheckedChanged += (_, _) => UpdateSelection();
            }
            TabPrograms.Text = $"Programme ({_win32.Count(i => i.App.Class != AppClass.System)})";
            TabStore.Text = $"Windows-Apps & Bloatware ({_store.Count(i => i.App.Class != AppClass.System)})";
            Apply();
            UpdateSelection();
            await MeasureSizesAsync();
            Apply();
        }
        catch (Exception ex)
        {
            Log.Write("Programmliste: " + ex);
        }
        finally
        {
            Busy.IsActive = false;
            _loading = false;
        }
    }

    /// <summary>Programme ohne Größenangabe und Store-Apps: Installationsordner messen.</summary>
    private async Task MeasureSizesAsync()
    {
        var todo = AllItems.Where(p => p.Size == 0 && !string.IsNullOrEmpty(p.App.InstallLocation)).ToList();
        if (todo.Count == 0) return;
        var dq = DispatcherQueue;
        await Task.Run(() => Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
        {
            string loc = p.App.InstallLocation;
            if (!p.App.IsStore && !LeftoverProfile.IsAcceptableInstallDir(Locations.Normalize(loc))) return;
            long size = DirectorySize.Compute(loc);
            if (size > 0) dq.TryEnqueue(() => p.UpdateSize(size));
        }));
        await Task.Delay(50);
    }

    private void Apply()
    {
        if (!_ready) return;
        IEnumerable<AppItem> q = StoreTab ? _store : _win32;
        string s = SearchBox.Text?.Trim() ?? "";
        if (s.Length > 0) q = q.Where(p => p.Matches(s));
        q = FilterCombo.SelectedIndex switch
        {
            1 => q.Where(p => p.App.Class == AppClass.Bloatware),
            2 => q.Where(p => p.App.Class == AppClass.Runtime),
            3 => q.Where(p => p.App.UninstallerMissing),
            4 => q,
            _ => q.Where(p => p.App.Class != AppClass.System),
        };
        q = SortCombo.SelectedIndex switch
        {
            1 => q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => q.OrderByDescending(p => p.App.InstallDate ?? DateTime.MinValue),
            3 => q.OrderBy(p => p.App.InstallDate ?? DateTime.MaxValue),
            _ => q.OrderByDescending(p => p.Size),
        };
        var list = q.ToList();
        AppList.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 && !_loading ? Visibility.Visible : Visibility.Collapsed;
        SelectBloatButton.IsEnabled = (StoreTab ? _store : _win32).Any(i => i.App.Class == AppClass.Bloatware);
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => Apply();
    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Apply();
    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) => Apply();

    /// <summary>Symbole erst laden, wenn eine Zeile sichtbar wird.</summary>
    private async void AppList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not AppItem item || item.IconRequested) return;
        item.IconRequested = true;
        try
        {
            if (item.App.IsStore)
            {
                item.Icon = await IconLoader.LoadImageFileAsync(item.App.LogoPath);
                return;
            }
            var (file, index) = IconSource(item.App);
            if (file.Length == 0) return;
            var pixels = await Task.Run(() => IconLoader.ReadIconPixels(file, index));
            item.Icon = IconLoader.CreateBitmap(pixels);
        }
        catch { }
    }

    private static (string File, int Index) IconSource(AppEntry app)
    {
        string raw = app.DisplayIcon.Trim();
        int index = 0;
        int comma = raw.LastIndexOf(',');
        if (comma > 2 && int.TryParse(raw[(comma + 1)..].Trim(), out int idx)) { index = idx; raw = raw[..comma]; }
        string file = AppCatalog.CleanPath(raw);
        if (file.Length > 0 && File.Exists(file)) return (file, index);

        if (app.InstallLocation.Length > 3 && Directory.Exists(app.InstallLocation))
        {
            try
            {
                string? exe = Directory.EnumerateFiles(app.InstallLocation, "*.exe")
                    .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                if (exe != null) return (exe, 0);
            }
            catch { }
        }
        var (uf, _) = UninstallRunner.Split(app.UninstallString);
        return uf.Contains('\\') && File.Exists(uf) ? (uf, 0) : ("", 0);
    }

    // ------------------------------------------------------------------ Auswahl

    private void UpdateSelection()
    {
        var sel = AllItems.Where(i => i.IsChecked).ToList();
        SelectedText.Text = sel.Count == 1 ? "1 Eintrag" : $"{sel.Count} Einträge";
        long size = sel.Sum(i => i.Size);
        SelectedHint.Text = sel.Count == 0
            ? "Haken setzen, um mehrere Programme auf einmal zu entfernen."
            : $"Belegt ca. {Fmt.Bytes(size)} – plus alle Reste, die nach der Deinstallation gefunden werden.";
        UninstallSelectedButton.IsEnabled = sel.Count > 0 && !_busy;
    }

    private void SelectBloat_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in (StoreTab ? _store : _win32).Where(i => i.App.Class == AppClass.Bloatware)) i.IsChecked = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in AllItems) i.IsChecked = false;
    }

    private void Options_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var s = AppSettings.Current;
        s.SilentUninstall = SilentToggle.IsOn;
        if (Elevation.IsAdmin)
        {
            s.RestorePoint = RestoreToggle.IsOn;
            s.StoreAllUsers = AllUsersToggle.IsOn;
        }
        s.Save();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AppItem p } && p.HasLocation) Elevation.ShowInExplorer(p.App.InstallLocation);
    }

    private void Admin_Click(object sender, RoutedEventArgs e) => AdminRestart.Restart();

    private void OpenLeftovers_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("leftovers");

    private void StopWait_Click(object sender, RoutedEventArgs e) => _waitCts?.Cancel();

    // ------------------------------------------------------------------ Deinstallation

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AppItem p }) await UninstallAsync(new List<AppItem> { p });
    }

    private async void UninstallSelected_Click(object sender, RoutedEventArgs e) =>
        await UninstallAsync(AllItems.Where(i => i.IsChecked && i.CanUninstall).ToList());

    private async Task UninstallAsync(List<AppItem> items)
    {
        if (_busy || items.Count == 0) return;
        items = items.Where(i => i.CanUninstall).ToList();
        if (items.Count == 0) return;

        if (!await ConfirmAsync(items)) return;

        _busy = true;
        UpdateSelection();
        ProgressCard.Visibility = Visibility.Visible;
        Info.IsOpen = false;
        bool silent = SilentToggle.IsOn;
        var removed = new List<LeftoverProfile>();
        int ok = 0, cancelled = 0, failed = 0, still = 0;

        try
        {
            // 1) Fingerabdrücke VOR der Deinstallation sichern
            SetProgress("Vorbereitung", "Programmdaten werden für die spätere Restesuche erfasst…", 0, items.Count);
            var profiles = await Task.Run(() => items.Select(i => LeftoverProfile.Create(i.App)).ToList());

            // 2) Wiederherstellungspunkt
            if (RestoreToggle.IsOn && Elevation.IsAdmin && items.Any(i => !i.App.IsStore && !i.App.UninstallerMissing))
            {
                SetProgress("Wiederherstellungspunkt", "Windows erstellt einen Wiederherstellungspunkt (kann eine Minute dauern)…", 0, items.Count);
                bool rp = await UninstallRunner.CreateRestorePointAsync("Simplex: vor Deinstallation von " + string.Join(", ", items.Take(3).Select(i => i.Name)));
                Log.Write("Wiederherstellungspunkt: " + (rp ? "erstellt" : "nicht möglich"));
            }

            // 3) Deinstallieren
            for (int n = 0; n < items.Count; n++)
            {
                var item = items[n];
                var profile = profiles[n];
                SetProgress($"{item.Name} ({n + 1}/{items.Count})", "Wird gestartet…", n, items.Count);
                _waitCts = new CancellationTokenSource();
                StopWaitButton.IsEnabled = !item.App.IsStore;

                if (item.App.IsStore)
                {
                    ProgressText.Text = "Store-App wird entfernt…";
                    var (success, error) = await AppCatalog.RemoveStoreAppAsync(item.App, AllUsersToggle.IsOn);
                    if (success) { ok++; removed.Add(profile); }
                    else { failed++; Log.Write($"Store-App {item.App.PackageFullName}: {error}"); }
                    continue;
                }

                var progress = new Progress<string>(t => ProgressText.Text = t);
                var status = await UninstallRunner.RunAsync(item.App, silent, progress, _waitCts.Token);
                Log.Write($"Deinstallation {item.Name}: {status}");
                switch (status)
                {
                    case UninstallStatus.Completed:
                    case UninstallStatus.UninstallerMissing:
                        ok++;
                        removed.Add(profile);
                        break;
                    case UninstallStatus.StillInstalled:
                        // Manche Deinstaller entfernen alles außer ihrem Eintrag → wenn der Deinstaller selbst weg ist, gilt es als entfernt.
                        if (item.App.MsiProductCode == null && !UninstallRunner.UninstallerExists(item.App.UninstallString)) { ok++; removed.Add(profile); }
                        else still++;
                        break;
                    case UninstallStatus.Cancelled:
                        cancelled++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }

            // 4) Restesuche
            List<LeftoverItem> leftovers = new();
            if (removed.Count > 0)
            {
                SetProgress("Restesuche", "Ordner, Registry, Dienste und Aufgaben werden durchsucht…", items.Count, items.Count);
                StopWaitButton.IsEnabled = false;
                var progress = new Progress<string>(t => ProgressText.Text = t);
                leftovers = await Task.Run(() => LeftoverScanner.Scan(removed, progress));
            }

            await LoadAsync();

            string summary = $"{ok} entfernt" + (still > 0 ? $", {still} noch installiert (abgebrochen?)" : "") +
                             (cancelled > 0 ? $", {cancelled} abgebrochen" : "") + (failed > 0 ? $", {failed} fehlgeschlagen" : "") + ".";
            if (leftovers.Count > 0)
            {
                ShowInfo(InfoBarSeverity.Success, "Deinstallation abgeschlossen", summary + $" {leftovers.Count} Reste gefunden – Vorschau wird angezeigt.");
                string title = removed.Count == 1 ? "Reste von „" + removed[0].App.Name + "“" : $"Reste von {removed.Count} Programmen";
                App.Window?.ShowLeftovers(new LeftoverSession { Title = title, Items = leftovers });
            }
            else
            {
                ShowInfo(ok > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    ok > 0 ? "Restlos entfernt" : "Nichts entfernt",
                    summary + (ok > 0 ? " Es wurden keine Reste gefunden – alles sauber." : ""));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Deinstallation: " + ex);
            ShowInfo(InfoBarSeverity.Error, "Fehler bei der Deinstallation", ex.Message);
        }
        finally
        {
            _waitCts = null;
            _busy = false;
            ProgressCard.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }

    private async Task<bool> ConfirmAsync(List<AppItem> items)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        string names = string.Join("\n", items.Take(12).Select(i => "•  " + i.Name)) + (items.Count > 12 ? $"\n…und {items.Count - 12} weitere" : "");
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = (items.Count == 1 ? "Dieses Programm wird deinstalliert:" : $"Diese {items.Count} Einträge werden nacheinander deinstalliert:") + "\n\n" + names,
        });
        if (items.Any(i => i.App.UninstallerMissing))
            panel.Children.Add(new InfoBar
            {
                IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational,
                Message = "Bei Einträgen ohne Deinstaller werden direkt nur die Reste gesucht.",
            });
        var risky = items.Where(i => i.App.Class == AppClass.Runtime).ToList();
        if (risky.Count > 0)
            panel.Children.Add(new InfoBar
            {
                IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Title = "Vorsicht",
                Message = string.Join("\n", risky.Take(5).Select(r => r.Name + ": " + r.App.ClassReason)),
            });
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "Anschließend werden alle Reste gesucht und Ihnen zur Auswahl angezeigt – gelöscht wird erst nach Ihrer Bestätigung.",
        });
        return await Dialogs.ConfirmAsync(XamlRoot, items.Count == 1 ? "Restlos deinstallieren?" : "Ausgewählte restlos deinstallieren?", panel, "Deinstallieren");
    }

    private void SetProgress(string title, string text, int done, int total)
    {
        ProgressTitle.Text = title;
        ProgressText.Text = text;
        StepBar.Maximum = Math.Max(1, total);
        StepBar.Value = done;
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        Info.Severity = severity;
        Info.Title = title;
        Info.Message = message;
        Info.IsOpen = true;
    }
}
