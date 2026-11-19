using System.Diagnostics;

namespace Simplex.Core.Uninstall;

/// <summary>
/// „Fingerabdruck“ eines Programms – wird VOR der Deinstallation erstellt, weil danach Installationsordner und
/// Exe-Dateien verschwunden sind. Enthält Namen, Hersteller, Exe-Namen und Produktnamen aus den Datei-Versionsinfos.
/// </summary>
public sealed class LeftoverProfile
{
    public required AppEntry App { get; init; }
    public string InstallDir { get; private set; } = "";
    public HashSet<string> NameKeys { get; } = new(StringComparer.Ordinal);
    public string PublisherKey { get; private set; } = "";
    public HashSet<string> ExeNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> s_genericLeaf = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "x64", "x86", "app", "current", "program", "uninstall", "uninst", "_uninst", "_uninstall", "uninstaller", "installer", "setup",
    };

    public static LeftoverProfile Create(AppEntry app)
    {
        var p = new LeftoverProfile { App = app };
        if (app.IsStore)
        {
            p.NameKeys.Add(NameKey.Norm(app.PackageFamilyName));
            return p;
        }

        p.InstallDir = DetermineInstallDir(app);
        foreach (var k in NameKey.KeysForName(app.Name, app.Publisher)) p.NameKeys.Add(k);
        p.PublisherKey = NameKey.PublisherKey(app.Publisher);

        if (p.InstallDir.Length > 0 && Directory.Exists(p.InstallDir))
        {
            string leaf = NameKey.Norm(Path.GetFileName(p.InstallDir));
            if (NameKey.IsUsable(leaf)) p.NameKeys.Add(leaf);
            try
            {
                var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
                foreach (var exe in Directory.EnumerateFiles(p.InstallDir, "*.exe", opts).Take(300))
                    p.AddExe(exe);
            }
            catch { }
        }

        string icon = KnownSoftwareIndex.IconPath(app.DisplayIcon);
        if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon))
        {
            p.AddExe(icon);
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(icon);
                foreach (var k in NameKey.KeysForName(vi.ProductName, vi.CompanyName)) p.NameKeys.Add(k);
                string ck = NameKey.PublisherKey(vi.CompanyName);
                if (p.PublisherKey.Length == 0) p.PublisherKey = ck;
            }
            catch { }
        }
        return p;
    }

    private void AddExe(string path)
    {
        string name = Path.GetFileName(path);
        string stem = Path.GetFileNameWithoutExtension(path);
        if (NameKey.GenericExe.Contains(stem) || stem.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) return;
        ExeNames.Add(name);
        string n = NameKey.Norm(stem);
        if (n.Length >= 4 && NameKey.IsUsable(n)) NameKeys.Add(n);
    }

    /// <summary>Installationsordner bestimmen – auch wenn der Eintrag keinen nennt (über Icon- oder Deinstaller-Pfad).</summary>
    private static string DetermineInstallDir(AppEntry app)
    {
        foreach (var candidate in Candidates(app))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            string dir = Locations.Normalize(candidate);
            while (s_genericLeaf.Contains(Path.GetFileName(dir)) && Locations.Depth(dir) > 2)
                dir = Path.GetDirectoryName(dir)!;
            if (IsAcceptableInstallDir(dir)) return dir;
        }
        return "";
    }

    private static IEnumerable<string> Candidates(AppEntry app)
    {
        yield return app.InstallLocation;
        string icon = KnownSoftwareIndex.IconPath(app.DisplayIcon);
        if (icon.Length > 3) yield return Path.GetDirectoryName(icon) ?? "";
        var (uf, _) = UninstallRunner.Split(app.UninstallString);
        if (uf.Contains('\\')) yield return Path.GetDirectoryName(AppCatalog.CleanPath(uf)) ?? "";
    }

    public static bool IsAcceptableInstallDir(string dir)
    {
        if (Locations.Depth(dir) < 2) return false;
        if (Locations.IsWindowsDir(dir)) return false;
        if (Locations.IsUnderOrEqual(dir, Locations.Temp)) return false;
        string leaf = Path.GetFileName(dir);
        if (leaf.StartsWith("{", StringComparison.Ordinal)) return false;
        if (dir.Contains(@"\Package Cache", StringComparison.OrdinalIgnoreCase)) return false;
        if (dir.Contains(@"\Installer", StringComparison.OrdinalIgnoreCase) && Locations.IsUnderOrEqual(dir, Locations.Windows)) return false;
        if (dir.Contains(@"\Common Files", StringComparison.OrdinalIgnoreCase)) return false;
        if (dir.Contains(@"\WindowsApps", StringComparison.OrdinalIgnoreCase)) return false;
        // Nie Wurzeln wie „C:\Program Files“, „AppData\Local“ usw.
        foreach (var (root, _) in Locations.LeftoverRoots())
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(dir, Locations.Profile, StringComparison.OrdinalIgnoreCase)) return false;
        if (Locations.Profile.Length > 0 && string.Equals(Path.GetDirectoryName(dir), Locations.Profile, StringComparison.OrdinalIgnoreCase) &&
            leaf is "AppData" or "Documents" or "Desktop" or "Downloads" or "OneDrive" or "Pictures" or "Music" or "Videos")
            return false;
        return true;
    }

    /// <summary>2 = Name passt exakt, 1 = ähnlich, 0 = nein.</summary>
    public int MatchName(string name)
    {
        string n = NameKey.Norm(name);
        if (!NameKey.IsUsable(n)) return 0;
        if (NameKeys.Contains(n)) return 2;
        string cleaned = NameKey.Norm(NameKey.CleanName(name));
        if (cleaned.Length > 0 && NameKeys.Contains(cleaned)) return 2;
        int best = 0;
        foreach (var k in NameKeys)
            best = Math.Max(best, NameKey.Match(n, k));
        return best;
    }

    public bool IsPublisher(string name) => PublisherKey.Length > 0 && NameKey.Norm(name) == PublisherKey;

    public bool IsUnderInstallDir(string? path) =>
        InstallDir.Length > 0 && !string.IsNullOrEmpty(path) && Locations.IsUnderOrEqual(Locations.Normalize(path), InstallDir);
}
