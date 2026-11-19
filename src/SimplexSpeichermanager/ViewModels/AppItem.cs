using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Simplex.Core;
using Simplex.Core.Uninstall;

namespace Simplex.ViewModels;

/// <summary>Ein Programm bzw. eine Store-App in der Deinstallationsliste.</summary>
public sealed class AppItem : Observable
{
    public AppItem(AppEntry app) { App = app; }

    public AppEntry App { get; }
    public string Name => App.Name;
    public string Initial => string.IsNullOrEmpty(App.Name) ? "?" : App.Name[..1].ToUpperInvariant();

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(App.Publisher)) parts.Add(App.Publisher);
            if (!string.IsNullOrEmpty(App.Version)) parts.Add("Version " + App.Version);
            if (App.InstallDate is { } d && d.Year > 1990) parts.Add("installiert am " + d.ToString("dd.MM.yyyy"));
            if (App.PerUser) parts.Add("nur für diesen Benutzer");
            return string.Join(" · ", parts);
        }
    }

    public string ClassReason => App.ClassReason;
    public Visibility ReasonVisibility => string.IsNullOrEmpty(App.ClassReason) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BloatVisibility => App.Class == AppClass.Bloatware ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RuntimeVisibility => App.Class == AppClass.Runtime ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SystemVisibility => App.Class == AppClass.System ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OrphanVisibility => App.UninstallerMissing ? Visibility.Visible : Visibility.Collapsed;

    public bool CanUninstall => App.CanUninstall;
    public double ItemOpacity => App.CanUninstall ? 1.0 : 0.55;
    public string ActionText => App.Class == AppClass.System ? "Gesperrt" : App.UninstallerMissing ? "Reste entfernen" : "Deinstallieren";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value && CanUninstall)) CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;

    public long Size => App.Size;
    public string SizeText => App.Size > 0 ? Fmt.Bytes(App.Size) : "–";

    public void UpdateSize(long size)
    {
        App.Size = size;
        Raise(nameof(Size));
        Raise(nameof(SizeText));
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { if (Set(ref _icon, value)) { Raise(nameof(InitialVisibility)); Raise(nameof(IconVisibility)); } }
    }
    public Visibility InitialVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IconVisibility => _icon == null ? Visibility.Collapsed : Visibility.Visible;
    public bool IconRequested { get; set; }

    public bool HasLocation => !App.IsStore && !string.IsNullOrEmpty(App.InstallLocation) && Directory.Exists(App.InstallLocation);

    public bool Matches(string search) =>
        App.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        App.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        App.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase);
}
