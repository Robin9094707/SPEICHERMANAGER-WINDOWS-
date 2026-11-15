using Simplex.Core;

namespace Simplex.ViewModels;

public sealed class DriveItem
{
    public DriveItem(DriveInfo d)
    {
        Root = d.RootDirectory.FullName;
        string letter = d.Name.TrimEnd('\\');
        string label = "";
        try { label = d.VolumeLabel; } catch { }
        if (string.IsNullOrWhiteSpace(label)) label = d.DriveType == DriveType.Removable ? "Wechseldatenträger" : "Lokaler Datenträger";
        Title = $"{label} ({letter})";
        Total = d.TotalSize;
        Free = d.AvailableFreeSpace;
        Used = Total - Free;
        UsedPercent = Total > 0 ? 100.0 * Used / Total : 0;
        IsCritical = UsedPercent >= 90;
        FreeText = $"{Fmt.Bytes(Free)} frei von {Fmt.Bytes(Total)}";
        UsedText = $"{UsedPercent:0} % belegt";
        string fs = "";
        try { fs = d.DriveFormat; } catch { }
        Subtitle = $"{(d.DriveType == DriveType.Removable ? "Wechseldatenträger" : "Festplatte")} · {fs}";
        Glyph = d.DriveType == DriveType.Removable ? "" : "";
    }

    public string Root { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public long Total { get; }
    public long Free { get; }
    public long Used { get; }
    public double UsedPercent { get; }
    public bool IsCritical { get; }
    public string FreeText { get; }
    public string UsedText { get; }
    public string Glyph { get; }

    public static List<DriveItem> Load()
    {
        var list = new List<DriveItem>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.TotalSize > 0)
                        list.Add(new DriveItem(d));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public override string ToString() => Title;
}
