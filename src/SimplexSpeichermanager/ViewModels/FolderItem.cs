using Microsoft.UI.Xaml;
using Simplex.Core;
using Simplex.Core.Safety;
using Simplex.Core.Scanning;

namespace Simplex.ViewModels;

public sealed class FolderItem
{
    public FolderItem(DirNode node, long parentTotal)
    {
        Node = node;
        Name = node.Parent == null ? node.FullPath : node.Name;
        SizeText = Fmt.Bytes(node.TotalSize);
        Percent = parentTotal > 0 ? 100.0 * node.TotalSize / parentTotal : 0;
        PercentText = Percent.ToString("0.0") + " %";
        DetailText = $"{Fmt.Number(node.TotalFiles)} Dateien · {Fmt.Number(node.TotalDirs)} Ordner";
        Glyph = node.Children is { Count: > 0 } ? "" : "";
        HiddenVisibility = node.IsHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pseudo-Eintrag für die Dateien direkt im aktuellen Ordner.</summary>
    public FolderItem(long ownSize, long ownFiles, long parentTotal)
    {
        Name = "(Dateien in diesem Ordner)";
        SizeText = Fmt.Bytes(ownSize);
        Percent = parentTotal > 0 ? 100.0 * ownSize / parentTotal : 0;
        PercentText = Percent.ToString("0.0") + " %";
        DetailText = $"{Fmt.Number(ownFiles)} Dateien";
        Glyph = "";
        HiddenVisibility = Visibility.Collapsed;
    }

    public DirNode? Node { get; }
    public string Name { get; }
    public string SizeText { get; }
    public double Percent { get; }
    public string PercentText { get; }
    public string DetailText { get; }
    public string Glyph { get; }
    public Visibility HiddenVisibility { get; }
    public string FullPath => Node?.FullPath ?? "";

    private SafetyVerdict? _safety;
    public SafetyVerdict Safety => _safety ??= Node == null ? new SafetyVerdict(SafetyLevel.Protected, "") : SafetyGuard.CheckDirectory(Node.FullPath);
}

public sealed class CrumbItem
{
    public CrumbItem(string name, DirNode? node) { Name = name; Node = node; }
    public string Name { get; }
    public DirNode? Node { get; }
    public override string ToString() => Name;
}
