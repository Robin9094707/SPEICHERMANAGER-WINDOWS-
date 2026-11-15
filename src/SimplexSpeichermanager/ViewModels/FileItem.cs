using Microsoft.UI.Xaml;
using Simplex.Core;
using Simplex.Core.Safety;
using Simplex.Core.Scanning;

namespace Simplex.ViewModels;

public sealed class FileItem
{
    public FileItem(FileEntry entry)
    {
        Entry = entry;
        Safety = SafetyGuard.CheckFile(entry.FullPath, entry.Attributes);
        Name = entry.Name;
        Directory = entry.Directory;
        SizeText = Fmt.Bytes(entry.Size);
        DateText = Fmt.Date(entry.LastWriteUtc);
        Glyph = Glyphs.For(entry.Category);
    }

    public FileEntry Entry { get; }
    public SafetyVerdict Safety { get; }

    public string Name { get; }
    public string Directory { get; }
    public string FullPath => Entry.FullPath;
    public long Size => Entry.Size;
    public string SizeText { get; }
    public string DateText { get; }
    public string Glyph { get; }
    public FileCategory Category => Entry.Category;

    public bool IsProtected => Safety.Level == SafetyLevel.Protected;
    public Visibility HiddenVisibility => Entry.IsHidden ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CautionVisibility => Safety.Level == SafetyLevel.Caution ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProtectedVisibility => Safety.Level == SafetyLevel.Protected ? Visibility.Visible : Visibility.Collapsed;
    public string SafetyReason => string.IsNullOrEmpty(Safety.Reason) ? "Kann gelöscht werden" : Safety.Reason;
    public double ContentOpacity => IsProtected ? 0.55 : 1.0;
}
