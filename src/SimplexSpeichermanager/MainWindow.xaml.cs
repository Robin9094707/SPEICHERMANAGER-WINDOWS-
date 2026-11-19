using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Simplex.Core;
using Simplex.Pages;
using Simplex.Services;
using Windows.Graphics;

namespace Simplex;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> s_pages = new()
    {
        ["dashboard"] = typeof(DashboardPage),
        ["scan"] = typeof(ScanPage),
        ["folders"] = typeof(FolderPage),
        ["cleanup"] = typeof(CleanupPage),
        ["games"] = typeof(GamesPage),
        ["programs"] = typeof(ProgramsPage),
        ["leftovers"] = typeof(LeftoversPage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch { }

        SizeAndCenter(1320, 860);

        AdminBadge.Visibility = Elevation.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        ApplyTheme(AppSettings.Current.Theme);
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtons();

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void SizeAndCenter(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scale = GetDpiForWindow(hwnd) / 96.0;
            int w = (int)(width * scale), h = (int)(height * scale);
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            w = Math.Min(w, area.Width);
            h = Math.Min(h, area.Height);
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void ApplyTheme(int theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var tb = AppWindow.TitleBar;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x20, 0, 0, 0);
        tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x30, 0, 0, 0);
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80);
    }

    /// <summary>Zeigt die Vorschau der gefundenen Reste im Reste-Finder an.</summary>
    public void ShowLeftovers(LeftoverSession session)
    {
        AppState.SetPendingLeftovers(session);
        NavigateTo("leftovers");
    }

    public void SetLeftoverBadge(int count)
    {
        LeftoverBadge.Value = count;
        LeftoverBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void NavigateTo(string tag)
    {
        if (tag == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if ((string?)item.Tag == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "dashboard";
        if (s_pages.TryGetValue(tag, out var type) && ContentFrame.CurrentSourcePageType != type)
            ContentFrame.Navigate(type, null, new EntranceNavigationTransitionInfo());
    }
}
