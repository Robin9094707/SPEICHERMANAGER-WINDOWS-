using Simplex.Core;
using Simplex.Core.Programs;

namespace Simplex.ViewModels;

public sealed class ProgramItem : Observable
{
    public ProgramItem(InstalledProgram p) { Program = p; }

    public InstalledProgram Program { get; }
    public string Name => Program.Name;
    public string Initial => string.IsNullOrEmpty(Program.Name) ? "?" : Program.Name[..1].ToUpperInvariant();

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Program.Publisher)) parts.Add(Program.Publisher);
            if (!string.IsNullOrEmpty(Program.Version)) parts.Add("Version " + Program.Version);
            if (Program.InstallDate is { } d) parts.Add("installiert am " + d.ToString("dd.MM.yyyy"));
            return string.Join(" · ", parts);
        }
    }

    public long Size => Program.Size;
    public string SizeText => Program.Size > 0 ? Fmt.Bytes(Program.Size) : "unbekannt";

    public void UpdateSize(long size)
    {
        Program.Size = size;
        Raise(nameof(Size));
        Raise(nameof(SizeText));
    }

    public bool HasLocation => !string.IsNullOrEmpty(Program.InstallLocation) && Directory.Exists(Program.InstallLocation);
}
