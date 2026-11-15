using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Simplex.Core;
using Simplex.Core.Programs;
using Simplex.Core.Scanning;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

public sealed partial class ProgramsPage : Page
{
    private List<ProgramItem> _all = new();
    private bool _loading;
    private bool _ready;

    public ProgramsPage()
    {
        InitializeComponent();
        SortCombo.SelectedIndex = 0;
        _ready = true;
        Loaded += async (_, _) => { if (_all.Count == 0) await LoadAsync(); };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        Busy.IsActive = true;
        var programs = await Task.Run(ProgramInventory.Load);
        _all = programs.Select(p => new ProgramItem(p)).ToList();
        Apply();

        // Programme ohne Größenangabe: Installationsordner messen
        var unknown = _all.Where(p => p.Size == 0 && p.HasLocation).ToList();
        if (unknown.Count > 0)
        {
            var dq = DispatcherQueue;
            await Task.Run(() => Parallel.ForEach(unknown, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
            {
                string loc = p.Program.InstallLocation;
                // Nie ganze Programmordner-Wurzeln messen (fehlerhafte Registry-Einträge)
                if (loc.TrimEnd('\\').Count(c => c == '\\') < 2) return;
                long size = DirectorySize.Compute(loc);
                dq.TryEnqueue(() => p.UpdateSize(size));
            }));
            await Task.Delay(50);
            Apply();
        }
        Busy.IsActive = false;
        _loading = false;
    }

    private void Apply()
    {
        if (!_ready) return;
        IEnumerable<ProgramItem> q = _all;
        string s = SearchBox.Text?.Trim() ?? "";
        if (s.Length > 0)
            q = q.Where(p => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || p.Program.Publisher.Contains(s, StringComparison.OrdinalIgnoreCase));
        q = SortCombo.SelectedIndex switch
        {
            1 => q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => q.OrderByDescending(p => p.Program.InstallDate ?? DateTime.MinValue),
            _ => q.OrderByDescending(p => p.Size),
        };
        var list = q.ToList();
        ProgramList.ItemsSource = list;
        SummaryText.Text = $"{list.Count} Programme · {Fmt.Bytes(list.Sum(p => p.Size))}";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Apply();
    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => Apply();

    private void Settings_Click(object sender, RoutedEventArgs e) => Elevation.ShellOpen("ms-settings:appsfeatures");

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProgramItem p } && p.HasLocation) Elevation.ShowInExplorer(p.Program.InstallLocation);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProgramItem p }) return;
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Der Deinstaller von „{p.Name}“ wird gestartet. Folgen Sie anschließend dessen Anweisungen.",
        };
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Programm deinstallieren?", text, "Deinstallieren")) return;
        bool ok = ProgramInventory.Uninstall(p.Program);
        Info.Severity = ok ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        Info.Title = ok ? "Deinstallation gestartet" : "Konnte Deinstaller nicht starten";
        Info.Message = ok ? "Klicken Sie nach Abschluss auf „Neu einlesen“." : "Versuchen Sie es über die Windows-Apps-Einstellungen.";
        Info.IsOpen = true;
    }
}
