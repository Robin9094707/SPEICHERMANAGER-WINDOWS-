using Microsoft.UI.Xaml;
using Simplex.Core;
using Simplex.Core.Uninstall;

namespace Simplex.ViewModels;

/// <summary>Ein gefundener Rest in der Vorschau.</summary>
public sealed class LeftoverVm : Observable
{
    public LeftoverVm(LeftoverItem item, bool isAdmin)
    {
        Item = item;
        CanRun = !item.RequiresAdmin || isAdmin;
        _isChecked = CanRun && item.Confidence != Confidence.Low;
    }

    public LeftoverItem Item { get; }
    public bool CanRun { get; }
    public double ItemOpacity => CanRun ? 1.0 : 0.55;
    public Visibility AdminVisibility => CanRun ? Visibility.Collapsed : Visibility.Visible;

    public string Title => Item.Kind switch
    {
        LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut => Item.Path,
        LeftoverKind.RegistryValue or LeftoverKind.FirewallRule => Item.Display,
        LeftoverKind.UninstallEntry => Item.Display,
        LeftoverKind.Service => "Dienst: " + Item.Display,
        LeftoverKind.ScheduledTask => "Aufgabe: " + Item.Display,
        _ => Item.Path,
    };

    public string Reason => Item.Reason;
    public string Owner => Item.Owner;

    public string Glyph => Item.Kind switch
    {
        LeftoverKind.Folder => "",
        LeftoverKind.File => "",
        LeftoverKind.Shortcut => "",
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => "",
        LeftoverKind.UninstallEntry => "",
        LeftoverKind.Service => "",
        LeftoverKind.ScheduledTask => "",
        LeftoverKind.FirewallRule => "",
        _ => "",
    };

    public string Group => Item.Kind switch
    {
        LeftoverKind.Folder or LeftoverKind.File => "Dateien & Ordner",
        LeftoverKind.Shortcut => "Verknüpfungen",
        LeftoverKind.UninstallEntry => "Einträge in „Apps & Features“",
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => "Registry",
        _ => "Dienste, Aufgaben & Firewall",
    };

    public int GroupOrder => Item.Kind switch
    {
        LeftoverKind.Folder or LeftoverKind.File => 0,
        LeftoverKind.UninstallEntry => 1,
        LeftoverKind.Shortcut => 2,
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => 3,
        _ => 4,
    };

    public string ConfidenceText => Item.Confidence switch
    {
        Confidence.High => "Sicher",
        Confidence.Medium => "Wahrscheinlich",
        _ => "Prüfen",
    };
    public Visibility HighVisibility => Item.Confidence == Confidence.High ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MediumVisibility => Item.Confidence == Confidence.Medium ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LowVisibility => Item.Confidence == Confidence.Low ? Visibility.Visible : Visibility.Collapsed;

    public string SizeText => Item.IsFileSystem ? (Item.Size > 0 ? Fmt.Bytes(Item.Size) : "0 B") : "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value && CanRun)) CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;

    public bool Matches(string s) =>
        Item.Path.Contains(s, StringComparison.OrdinalIgnoreCase) || Item.Owner.Contains(s, StringComparison.OrdinalIgnoreCase) ||
        Item.Display.Contains(s, StringComparison.OrdinalIgnoreCase);
}

public sealed class LeftoverGroup : List<LeftoverVm>
{
    public LeftoverGroup(string key, IEnumerable<LeftoverVm> items) : base(items)
    {
        Key = key;
        long size = this.Sum(i => i.Item.IsFileSystem ? i.Item.Size : 0);
        Header = $"{key} ({Count})" + (size > 0 ? " · " + Fmt.Bytes(size) : "");
    }

    public string Key { get; }
    public string Header { get; }
}
