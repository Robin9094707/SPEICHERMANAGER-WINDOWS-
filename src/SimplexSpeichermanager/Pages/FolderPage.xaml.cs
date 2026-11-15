using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Simplex.Core;
using Simplex.Core.Safety;
using Simplex.Core.Scanning;
using Simplex.Services;
using Simplex.ViewModels;

namespace Simplex.Pages;

public sealed partial class FolderPage : Page
{
    private int _version = -1;
    private DirNode? _current; // null = Übersicht aller Scan-Wurzeln

    public FolderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_version != AppState.ScanVersion)
        {
            _version = AppState.ScanVersion;
            var scan = AppState.LastScan;
            _current = scan?.Roots.Count == 1 ? scan.Roots[0] : null;
        }
        Show(_current);
    }

    private void Show(DirNode? node)
    {
        var scan = AppState.LastScan;
        if (scan == null || scan.Roots.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            FolderList.ItemsSource = null;
            Crumbs.ItemsSource = null;
            SummaryText.Text = "";
            UpButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        _current = node;

        var items = new List<FolderItem>();
        if (node == null)
        {
            long sum = scan.Roots.Sum(r => r.TotalSize);
            items.AddRange(scan.Roots.OrderByDescending(r => r.TotalSize).Select(r => new FolderItem(r, sum)));
            SummaryText.Text = $"{scan.Roots.Count} Speicherorte · {Fmt.Bytes(sum)}";
        }
        else
        {
            if (node.Children != null)
                items.AddRange(node.Children.Where(c => !c.IsDeleted).OrderByDescending(c => c.TotalSize).Select(c => new FolderItem(c, node.TotalSize)));
            if (node.OwnFiles > 0)
            {
                var own = new FolderItem(node.OwnSize, node.OwnFiles, node.TotalSize);
                int idx = items.FindIndex(i => i.Percent < own.Percent);
                items.Insert(idx < 0 ? items.Count : idx, own);
            }
            SummaryText.Text = $"{node.FullPath} · {Fmt.Bytes(node.TotalSize)} · {Fmt.Number(node.TotalFiles)} Dateien in {Fmt.Number(node.TotalDirs)} Ordnern";
        }
        FolderList.ItemsSource = items;

        // Brotkrumen
        var crumbs = new List<CrumbItem>();
        for (var n = node; n != null; n = n.Parent)
            crumbs.Insert(0, new CrumbItem(n.Parent == null ? n.FullPath.TrimEnd('\\') : n.Name, n));
        if (scan.Roots.Count > 1) crumbs.Insert(0, new CrumbItem("Alle Orte", null));
        Crumbs.ItemsSource = crumbs;

        UpButton.IsEnabled = node != null && (node.Parent != null || scan.Roots.Count > 1);
        DeleteButton.IsEnabled = false;
    }

    private void FolderList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FolderList.SelectedItem is FolderItem { Node: { } n } && n.Children is { Count: > 0 }) Show(n);
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = FolderList.SelectedItem as FolderItem;
        DeleteButton.IsEnabled = item?.Node != null && item.Safety.Level != SafetyLevel.Protected;
        if (item?.Node != null)
            ToolTipService.SetToolTip(DeleteButton, item.Safety.Level == SafetyLevel.Safe ? "Ausgewählten Ordner löschen" : item.Safety.Reason);
    }

    private void Crumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is CrumbItem c) Show(c.Node);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Show(_current?.Parent);

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        var target = (FolderList.SelectedItem as FolderItem)?.Node ?? _current;
        if (target != null) Elevation.ShowInExplorer(target.FullPath);
    }

    private void GoScan_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("scan");

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not FolderItem { Node: { } node } item) return;
        var verdict = item.Safety;
        if (verdict.Level == SafetyLevel.Protected) return;

        string path = node.FullPath;
        string? warn = verdict.Level == SafetyLevel.Caution ? verdict.Reason : null;
        bool? recycle = await Dialogs.ConfirmDeleteAsync(XamlRoot, "Ordner löschen?",
            $"„{path}“ mit {Fmt.Number(node.TotalFiles)} Dateien ({Fmt.Bytes(node.TotalSize)}) wird gelöscht.", warn);
        if (recycle == null) return;

        long size = node.TotalSize;
        var outcome = await Task.Run(() => Deleter.Delete(new[] { new DeleteRequest(path, size, FileAttributes.Directory, IsDirectory: true) }, recycle.Value));
        if (outcome.Deleted > 0)
        {
            node.RemoveFromTree();
            AppState.LastScan?.LargeFiles.RemoveAll(f => f.FullPath.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));
            if (!recycle.Value) AppState.AddFreed(size);
            ShowInfo(InfoBarSeverity.Success, "Ordner gelöscht",
                $"{Fmt.Bytes(size)} {(recycle.Value ? "in den Papierkorb verschoben" : "freigegeben")}.");
        }
        else
        {
            ShowInfo(InfoBarSeverity.Error, "Löschen fehlgeschlagen",
                outcome.SkippedProtected > 0 ? "Ordner ist geschützt." : string.Join("; ", outcome.Errors.Take(2)) +
                " – Evtl. werden Dateien gerade verwendet. Teile des Ordners wurden ggf. bereits gelöscht.");
        }
        Show(_current);
    }

    private void ShowInfo(InfoBarSeverity s, string title, string msg)
    {
        Info.Severity = s;
        Info.Title = title;
        Info.Message = msg;
        Info.IsOpen = true;
    }
}
