using System.Diagnostics;
using Microsoft.Win32;
using Simplex.Core.Games;
using Simplex.Core.Native;

namespace Simplex.Core.Uninstall;

/// <summary>
/// Wissen über alles, was auf dem PC noch installiert ist bzw. läuft: Namen, Hersteller, Exe-Namen und Ordner.
/// Damit wird verhindert, dass Ordner gelöscht werden, die ein anderes (noch installiertes) Programm nutzt.
/// </summary>
public sealed class KnownSoftwareIndex
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<string> _paths = new();
    private readonly HashSet<string> _packageFamilies = new(StringComparer.OrdinalIgnoreCase);
    private string[] _keyArray = Array.Empty<string>();
    private string[] _excludeDirs = Array.Empty<string>();

    public int PackageCount => _packageFamilies.Count;
    public IReadOnlyCollection<string> Keys => _keys;

    /// <summary>
    /// Baut den Index auf. Einträge in <paramref name="excludeRegistryPaths"/> / <paramref name="excludePackages"/>
    /// (die gerade deinstallierten Programme) werden ignoriert.
    /// </summary>
    public static KnownSoftwareIndex Build(ICollection<string>? excludeRegistryPaths = null, ICollection<string>? excludePackages = null,
        ICollection<string>? excludeDirs = null, bool includeProcesses = true, bool includeGames = true)
    {
        var idx = new KnownSoftwareIndex();
        idx._excludeDirs = (excludeDirs ?? Array.Empty<string>()).Where(d => Locations.Depth(d) >= 2).Select(Locations.Normalize).ToArray();
        excludeRegistryPaths ??= Array.Empty<string>();
        excludePackages ??= Array.Empty<string>();

        foreach (var r in AppCatalog.ReadUninstallRecords())
        {
            if (excludeRegistryPaths.Contains(r.RegistryPath, StringComparer.OrdinalIgnoreCase)) continue;
            if (r.Name.Length == 0 && r.InstallLocation.Length == 0) continue;
            // Verwaiste Einträge (Deinstaller und Ordner fehlen) zählen nicht als installiert
            bool locExists = r.InstallLocation.Length > 3 && Directory.Exists(r.InstallLocation);
            if (!locExists && r.UninstallString.Length > 0 && !r.WindowsInstaller && !UninstallRunner.UninstallerExists(r.UninstallString)) continue;

            foreach (var k in NameKey.KeysForName(r.Name, r.Publisher)) idx._keys.Add(k);
            string pk = NameKey.PublisherKey(r.Publisher);
            if (pk.Length > 0) idx._keys.Add(pk);
            if (locExists) idx.AddDirectory(r.InstallLocation, withExeNames: true);
            string icon = IconPath(r.DisplayIcon);
            if (icon.Length > 0 && File.Exists(icon)) idx.AddDirectory(Path.GetDirectoryName(icon)!, withExeNames: false);
            var (uf, _) = UninstallRunner.Split(r.UninstallString);
            if (uf.Contains('\\') && File.Exists(uf)) idx.AddPath(Path.GetDirectoryName(uf)!);
        }

        foreach (var p in AppCatalog.ReadStorePackages(includeFrameworks: true))
        {
            idx._packageFamilies.Add(p.FamilyName);
            if (excludePackages.Contains(p.FullName, StringComparer.OrdinalIgnoreCase)) continue;
            idx.AddKey(p.Name);
            idx.AddKey(p.DisplayName);
            int dot = p.Name.LastIndexOf('.');
            if (dot > 0) idx.AddKey(p.Name[(dot + 1)..]);
        }

        // Dienste und Autostart verweisen auf aktive Programmordner
        foreach (var (_, image) in Services())
            if (image.Length > 0 && File.Exists(image)) idx.AddPath(Path.GetDirectoryName(image)!);
        foreach (var (_, _, data) in RunEntries())
        {
            string exe = ExtractExe(data);
            if (exe.Length > 0 && File.Exists(exe)) idx.AddPath(Path.GetDirectoryName(exe)!);
        }

        if (includeProcesses)
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string? f = proc.MainModule?.FileName;
                    if (f != null) idx.AddPath(Path.GetDirectoryName(f)!);
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }

        if (includeGames)
        {
            try
            {
                foreach (var g in GameDetector.Detect())
                {
                    idx.AddPath(g.InstallPath);
                    idx.AddKey(g.Name);
                    idx.AddKey(Path.GetFileName(g.InstallPath));
                }
            }
            catch { }
        }

        // Gültige Startmenü-/Desktop-Verknüpfungen (z. B. portable Programme)
        foreach (var lnk in Locations.ShortcutFiles())
        {
            string t = ShellLink.GetTarget(lnk);
            if (t.Length > 3 && File.Exists(t)) idx.AddPath(Path.GetDirectoryName(t)!);
        }

        idx._keyArray = idx._keys.ToArray();
        return idx;
    }

    private void AddKey(string? name)
    {
        string n = NameKey.Norm(NameKey.CleanName(name));
        if (NameKey.IsUsable(n)) _keys.Add(n);
    }

    private void AddPath(string dir)
    {
        dir = Locations.Normalize(dir);
        if (Locations.Depth(dir) < 2) return;          // nie „C:\Program Files“ selbst
        if (Locations.IsWindowsDir(dir)) return;
        foreach (var ex in _excludeDirs)
            if (Locations.IsUnderOrEqual(dir, ex)) return;
        _paths.Add(dir);
    }

    private void AddDirectory(string dir, bool withExeNames)
    {
        dir = Locations.Normalize(dir);
        if (Locations.Depth(dir) < 2 || Locations.IsWindowsDir(dir)) return;
        foreach (var ex in _excludeDirs)
            if (Locations.IsUnderOrEqual(dir, ex)) return;
        AddPath(dir);
        AddKey(Path.GetFileName(dir));
        if (!withExeNames) return;
        try
        {
            foreach (var exe in Directory.EnumerateFiles(dir, "*.exe").Take(40))
            {
                string n = Path.GetFileNameWithoutExtension(exe);
                if (!NameKey.GenericExe.Contains(n)) AddKey(n);
            }
        }
        catch { }
    }

    /// <summary>Liegt ein bekanntes (aktives) Programm in diesem Ordner oder ist der Ordner Teil eines solchen?</summary>
    public bool IsPathInUse(string dir)
    {
        dir = Locations.Normalize(dir);
        foreach (var p in _paths)
            if (Locations.IsUnderOrEqual(p, dir) || Locations.IsUnderOrEqual(dir, p)) return true;
        return false;
    }

    /// <summary>Enthält der Ordner ein noch genutztes Programm (aber ist nicht selbst Teil davon)?</summary>
    public bool ContainsPathInUse(string dir)
    {
        dir = Locations.Normalize(dir);
        foreach (var p in _paths)
            if (Locations.IsUnderOrEqual(p, dir)) return true;
        return false;
    }

    /// <summary>Passt ein Ordner-/Schlüsselname zu einem installierten Programm? 2 = exakt, 1 = ähnlich, 0 = nein.</summary>
    public int MatchName(string name)
    {
        string n = NameKey.Norm(name);
        if (n.Length == 0) return 0;
        if (_keys.Contains(n)) return 2;
        string cleaned = NameKey.Norm(NameKey.CleanName(name));
        if (cleaned.Length > 0 && _keys.Contains(cleaned)) return 2;
        int best = 0;
        foreach (var k in _keyArray)
        {
            int m = NameKey.Match(n, k);
            if (m > best) { best = m; if (best == 2) break; }
        }
        return best;
    }

    public bool IsPackageInstalled(string familyName) => _packageFamilies.Contains(familyName);

    // ------------- gemeinsame Helfer -------------

    public static string IconPath(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return "";
        string s = displayIcon.Trim();
        int comma = s.LastIndexOf(',');
        if (comma > 2 && int.TryParse(s[(comma + 1)..].Trim(), out _)) s = s[..comma];
        return AppCatalog.CleanPath(s);
    }

    /// <summary>Extrahiert die Exe aus einer Befehlszeile ("C:\x\y.exe" -arg → C:\x\y.exe).</summary>
    public static string ExtractExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        var (f, _) = UninstallRunner.Split(command);
        return f.Contains('\\') ? AppCatalog.CleanPath(f) : "";
    }

    /// <summary>Alle Dienste mit Pfad zur ausführbaren Datei.</summary>
    public static IEnumerable<(string Name, string Image)> Services()
    {
        var list = new List<(string, string)>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root == null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("ImagePath") is not string img) continue;
                    if (k.GetValue("Type") is int type && type is 1 or 2 or 8) continue; // Kernel-/Dateisystemtreiber
                    string exe = img.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) || img.StartsWith(@"\??\", StringComparison.Ordinal)
                        ? "" : ExtractExe(img);
                    list.Add((name, exe));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    /// <summary>Autostart-Einträge (Run-Schlüssel).</summary>
    public static IEnumerable<(string KeyPath, string ValueName, string Data)> RunEntries()
    {
        var list = new List<(string, string, string)>();
        foreach (var key in new[]
        {
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
        })
        {
            using var k = RegPath.Open(key);
            if (k == null) continue;
            try
            {
                foreach (var v in k.GetValueNames())
                    if (k.GetValue(v) is string data) list.Add((key, v, data));
            }
            catch { }
        }
        return list;
    }
}
