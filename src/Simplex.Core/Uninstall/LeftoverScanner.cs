using System.Text.RegularExpressions;
using Simplex.Core.Native;
using Simplex.Core.Safety;
using Simplex.Core.Scanning;

namespace Simplex.Core.Uninstall;

/// <summary>
/// Sucht nach einer Deinstallation alle Reste: Installationsordner, AppData/ProgramData, Temp, Startmenü und Desktop,
/// Registry (Software, App Paths, Autostart, Uninstall-Eintrag), Dienste, geplante Aufgaben und Firewall-Regeln.
/// Alles, was noch von einem anderen installierten Programm genutzt wird, wird ausgelassen.
/// </summary>
public static class LeftoverScanner
{
    private static readonly string[] s_softwareRoots =
    {
        @"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node", @"HKCU\Software\WOW6432Node",
    };

    private const string FirewallKey = @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    public static List<LeftoverItem> Scan(IReadOnlyList<LeftoverProfile> profiles, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Installierte Software wird erfasst…");
        var index = KnownSoftwareIndex.Build(
            profiles.Select(p => p.App.RegistryPath).Where(p => p.Length > 0).ToList(),
            profiles.Select(p => p.App.PackageFullName).Where(p => p.Length > 0).ToList(),
            profiles.Select(p => p.InstallDir).Where(p => p.Length > 0).ToList());

        var items = new List<LeftoverItem>();
        var tasks = profiles.Any(p => !p.App.IsStore) ? ReadTasks() : new List<TaskInfo>();
        var services = profiles.Any(p => !p.App.IsStore) ? KnownSoftwareIndex.Services().ToList() : new();

        foreach (var p in profiles)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Reste von „" + p.App.Name + "“ werden gesucht…");
            if (p.App.IsStore) ScanStore(p, items);
            else ScanWin32(p, index, tasks, services, items);
        }

        progress?.Report("Größen werden berechnet…");
        return Finish(items, ct);
    }

    private static void ScanStore(LeftoverProfile p, List<LeftoverItem> items)
    {
        if (Locations.Packages.Length == 0 || p.App.PackageFamilyName.Length == 0) return;
        string dir = Path.Combine(Locations.Packages, p.App.PackageFamilyName);
        if (Directory.Exists(dir) && !AppCatalog.StorePackageInstalled(p.App.PackageFamilyName))
            items.Add(Folder(dir, Confidence.High, "App-Daten der entfernten Store-App", p.App.Name));
    }

    private static void ScanWin32(LeftoverProfile p, KnownSoftwareIndex index, List<TaskInfo> tasks, List<(string Name, string Image)> services, List<LeftoverItem> items)
    {
        string owner = p.App.Name;

        // 1) Installationsordner
        if (p.InstallDir.Length > 0 && Directory.Exists(p.InstallDir) && !index.ContainsPathInUse(p.InstallDir) &&
            SafetyGuard.CheckDirectory(p.InstallDir).Level != SafetyLevel.Protected)
            items.Add(Folder(p.InstallDir, Confidence.High, "Installationsordner – nach der Deinstallation übrig geblieben", owner));

        // 2) Uninstall-Eintrag
        if (p.App.RegistryPath.Length > 0 && RegPath.Exists(p.App.RegistryPath))
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.UninstallEntry, Path = p.App.RegistryPath, Display = p.App.RegistryPath, Confidence = Confidence.High,
                Reason = "Eintrag in „Apps & Features“ ist noch vorhanden", Owner = owner,
                RequiresAdmin = p.App.RegistryPath.StartsWith("HKLM", StringComparison.Ordinal),
            });

        // 3) Ordner in Program Files, ProgramData, AppData, LocalLow
        foreach (var (root, kind) in Locations.LeftoverRoots())
            ScanFolderRoot(root, kind, p, index, items, owner);

        // 4) Temp
        if (Directory.Exists(Locations.Temp))
            foreach (var d in SafeDirs(Locations.Temp))
                if (p.MatchName(Path.GetFileName(d)) == 2)
                    items.Add(Folder(d, Confidence.High, "Temporäre Dateien des Programms", owner));

        // 5) Startmenü & Desktop
        ScanShortcuts(p, items, owner);

        // 6) Registry: Software\Hersteller\Programm
        foreach (var root in s_softwareRoots)
            ScanRegistryRoot(root, p, index, items, owner);

        // 7) App Paths, Classes\Applications
        foreach (var ap in new[]
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths",
        })
        {
            using var k = RegPath.Open(ap);
            if (k == null) continue;
            foreach (var sub in SafeSubKeys(k))
            {
                string exePath = "";
                try { using var s = k.OpenSubKey(sub); exePath = AppCatalog.CleanPath(s?.GetValue("") as string); } catch { }
                bool mine = p.IsUnderInstallDir(exePath) || (p.ExeNames.Contains(sub) && (exePath.Length == 0 || !File.Exists(exePath)));
                if (mine) items.Add(RegKey(RegPath.Combine(ap, sub), Confidence.High, "Programmpfad-Registrierung (App Paths)", owner));
            }
        }
        foreach (var exe in p.ExeNames)
        {
            string key = @"HKCU\Software\Classes\Applications\" + exe;
            if (RegPath.Exists(key)) items.Add(RegKey(key, Confidence.Medium, "Dateizuordnung „Öffnen mit“", owner));
        }

        // 8) Autostart
        foreach (var (key, value, data) in KnownSoftwareIndex.RunEntries())
        {
            string exe = KnownSoftwareIndex.ExtractExe(data);
            bool mine = p.IsUnderInstallDir(exe) || (exe.Length > 0 && !File.Exists(exe) && p.ExeNames.Contains(Path.GetFileName(exe)));
            if (mine)
                items.Add(new LeftoverItem
                {
                    Kind = LeftoverKind.RegistryValue, Path = key, ValueName = value, Display = key + " → " + value, Confidence = Confidence.High,
                    Reason = "Autostart-Eintrag", Owner = owner, RequiresAdmin = key.StartsWith("HKLM", StringComparison.Ordinal),
                });
        }

        // 9) Dienste
        foreach (var (name, image) in services)
        {
            if (image.Length == 0 || Locations.IsWindowsDir(image)) continue;
            bool mine = p.IsUnderInstallDir(image) || (!File.Exists(image) && p.ExeNames.Contains(Path.GetFileName(image)));
            if (mine)
                items.Add(new LeftoverItem
                {
                    Kind = LeftoverKind.Service, Path = name, Display = name + " (" + image + ")", Confidence = Confidence.High,
                    Reason = "Windows-Dienst des Programms", Owner = owner, RequiresAdmin = true,
                });
        }

        // 10) Geplante Aufgaben
        foreach (var t in tasks)
        {
            if (t.Command.Length == 0 || Locations.IsWindowsDir(t.Command)) continue;
            bool mine = p.IsUnderInstallDir(t.Command) || (!File.Exists(t.Command) && p.ExeNames.Contains(Path.GetFileName(t.Command))) ||
                        (!File.Exists(t.Command) && p.MatchName(Path.GetFileName(t.TaskPath)) == 2);
            if (mine)
                items.Add(new LeftoverItem
                {
                    Kind = LeftoverKind.ScheduledTask, Path = t.TaskPath, Display = t.TaskPath + " → " + t.Command, Confidence = Confidence.High,
                    Reason = "Geplante Aufgabe (z. B. Updater)", Owner = owner, RequiresAdmin = true,
                });
        }

        // 11) Firewall-Regeln
        if (p.InstallDir.Length > 0)
        {
            foreach (var (value, app) in FirewallRules())
                if (p.IsUnderInstallDir(app))
                    items.Add(new LeftoverItem
                    {
                        Kind = LeftoverKind.FirewallRule, Path = FirewallKey, ValueName = value, Display = "Firewall-Regel für " + Path.GetFileName(app),
                        Confidence = Confidence.High, Reason = "Firewall-Ausnahme für " + app, Owner = owner, RequiresAdmin = true,
                    });
        }
    }

    private static void ScanFolderRoot(string root, RootKind kind, LeftoverProfile p, KnownSoftwareIndex index, List<LeftoverItem> items, string owner)
    {
        foreach (var dir in SafeDirs(root))
        {
            string name = Path.GetFileName(dir);
            if (kind == RootKind.Local && (name.Equals("Programs", StringComparison.OrdinalIgnoreCase) || name.Equals("Packages", StringComparison.OrdinalIgnoreCase) ||
                                           name.Equals("Temp", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (p.InstallDir.Length > 0 && (Locations.IsUnderOrEqual(dir, p.InstallDir) || Locations.IsUnderOrEqual(p.InstallDir, dir)) &&
                !string.Equals(dir, p.InstallDir, StringComparison.OrdinalIgnoreCase))
            {
                // Herstellerordner, der den Installationsordner enthält → ggf. leeren Herstellerordner mitnehmen
                if (Locations.IsUnderOrEqual(p.InstallDir, dir)) TryPublisherFolder(dir, p, index, items, owner, kind);
                continue;
            }
            if (string.Equals(dir, p.InstallDir, StringComparison.OrdinalIgnoreCase)) continue;

            int m = p.MatchName(name);
            if (m > 0)
            {
                if (!Allowed(dir, name, m, index)) continue;
                items.Add(Folder(dir, m == 2 ? Confidence.High : Confidence.Medium,
                    (m == 2 ? "Ordner mit dem Namen des Programms" : "Ordner mit ähnlichem Namen") + " in " + RootName(kind), owner));
            }
            else if (p.IsPublisher(name))
            {
                TryPublisherFolder(dir, p, index, items, owner, kind);
            }
        }
    }

    /// <summary>Herstellerordner (z. B. AppData\Mozilla): passende Unterordner, und den Ordner selbst, wenn sonst nichts darin liegt.</summary>
    private static void TryPublisherFolder(string dir, LeftoverProfile p, KnownSoftwareIndex index, List<LeftoverItem> items, string owner, RootKind kind)
    {
        var children = SafeDirs(dir).ToList();
        var matched = new List<LeftoverItem>();
        bool othersRemain = false;
        foreach (var c in children)
        {
            string cn = Path.GetFileName(c);
            if (p.InstallDir.Length > 0 && string.Equals(c, p.InstallDir, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(Folder(c, Confidence.High, "Installationsordner", owner));
                continue;
            }
            int m = p.MatchName(cn);
            if (m > 0 && Allowed(c, cn, m, index))
                matched.Add(Folder(c, m == 2 ? Confidence.High : Confidence.Medium, "Programmordner im Herstellerordner (" + RootName(kind) + ")", owner));
            else
                othersRemain = true;
        }

        bool hasFiles;
        try { hasFiles = Directory.EnumerateFiles(dir).Any(); } catch { hasFiles = true; }
        bool publisherShared = index.MatchName(Path.GetFileName(dir)) == 2 || index.ContainsPathInUse(dir);

        if (!othersRemain && !publisherShared && !hasFiles && (matched.Count > 0 || children.Count == 0) &&
            SafetyGuard.CheckDirectory(dir).Level != SafetyLevel.Protected && NameKey.IsUsable(NameKey.Norm(Path.GetFileName(dir))))
        {
            items.Add(Folder(dir, Confidence.Medium, "Herstellerordner – enthält nur Reste dieses Programms", owner));
        }
        else
        {
            foreach (var m in matched)
                if (!m.Path.Equals(p.InstallDir, StringComparison.OrdinalIgnoreCase)) items.Add(m);
        }
    }

    private static bool Allowed(string dir, string name, int myMatch, KnownSoftwareIndex index)
    {
        if (Locations.SystemFolderNames.Contains(name) && myMatch < 2) return false;
        if (index.IsPathInUse(dir)) return false;
        int other = index.MatchName(name);
        if (other == 2 || (myMatch == 1 && other >= 1)) return false;
        return SafetyGuard.CheckDirectory(dir).Level != SafetyLevel.Protected;
    }

    private static void ScanShortcuts(LeftoverProfile p, List<LeftoverItem> items, string owner)
    {
        foreach (var root in Locations.StartMenus)
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            foreach (var d in SafeDirs(root))
            {
                string name = Path.GetFileName(d);
                if (name.Equals("Startup", StringComparison.OrdinalIgnoreCase) || name.Equals("Autostart", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.MatchName(name) == 2 || p.IsPublisher(name))
                {
                    // Nur, wenn alle Verknüpfungen darin tot sind oder zu diesem Programm gehören
                    bool allMine = true;
                    try
                    {
                        foreach (var lnk in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                        {
                            if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !lnk.EndsWith(".url", StringComparison.OrdinalIgnoreCase) &&
                                !lnk.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) { allMine = false; break; }
                            if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                            string t = ShellLink.GetTarget(lnk);
                            if (t.Length > 0 && File.Exists(t) && !p.IsUnderInstallDir(t)) { allMine = false; break; }
                        }
                    }
                    catch { allMine = false; }
                    if (allMine) items.Add(Folder(d, Confidence.High, "Startmenü-Ordner", owner, LeftoverKind.Folder));
                }
            }
        }

        foreach (var lnk in Locations.ShortcutFiles())
        {
            string t = ShellLink.GetTarget(lnk);
            string name = Path.GetFileNameWithoutExtension(lnk);
            bool mine = (t.Length > 0 && p.IsUnderInstallDir(t) && !File.Exists(t)) ||
                        (t.Length > 0 && p.IsUnderInstallDir(t)) ||
                        (t.Length > 0 && !File.Exists(t) && !Directory.Exists(t) && (p.MatchName(name) == 2 || p.ExeNames.Contains(Path.GetFileName(t))));
            if (mine)
                items.Add(new LeftoverItem
                {
                    Kind = LeftoverKind.Shortcut, Path = lnk, Display = lnk, Confidence = Confidence.High,
                    Reason = "Verknüpfung zeigt auf " + t, Owner = owner, RequiresAdmin = !lnk.StartsWith(Locations.Profile, StringComparison.OrdinalIgnoreCase),
                });
        }
    }

    private static void ScanRegistryRoot(string root, LeftoverProfile p, KnownSoftwareIndex index, List<LeftoverItem> items, string owner)
    {
        using var k = RegPath.Open(root);
        if (k == null) return;
        foreach (var sub in SafeSubKeys(k))
        {
            string full = RegPath.Combine(root, sub);
            int m = p.MatchName(sub);
            if (m > 0)
            {
                int other = index.MatchName(sub);
                if (other == 2 || (m == 1 && other >= 1)) continue;
                if (!RegPath.CanDeleteKey(full)) continue;
                items.Add(RegKey(full, m == 2 ? Confidence.High : Confidence.Medium, "Registry-Schlüssel mit Programmeinstellungen", owner));
            }
            else if (p.IsPublisher(sub))
            {
                using var pk = k.OpenSubKey(sub);
                if (pk == null) continue;
                var children = SafeSubKeys(pk);
                var mine = new List<string>();
                foreach (var c in children)
                {
                    int cm = p.MatchName(c);
                    int other = index.MatchName(c);
                    if (cm > 0 && other < 2 && !(cm == 1 && other >= 1)) mine.Add(c);
                }
                bool shared = index.MatchName(sub) == 2;
                bool hasValues;
                try { hasValues = pk.ValueCount > 0; } catch { hasValues = true; }
                if (!shared && mine.Count == children.Count && mine.Count > 0 && !hasValues && RegPath.CanDeleteKey(full))
                    items.Add(RegKey(full, Confidence.Medium, "Hersteller-Schlüssel – enthält nur Daten dieses Programms", owner));
                else
                    foreach (var c in mine)
                    {
                        string cf = RegPath.Combine(full, c);
                        if (RegPath.CanDeleteKey(cf)) items.Add(RegKey(cf, Confidence.High, "Registry-Schlüssel mit Programmeinstellungen", owner));
                    }
            }
        }
    }

    // ------------------------------------------------------------------
    // Gemeinsame Helfer
    // ------------------------------------------------------------------

    internal static LeftoverItem Folder(string dir, Confidence c, string reason, string owner, LeftoverKind kind = LeftoverKind.Folder) => new()
    {
        Kind = kind, Path = dir, Display = dir, Confidence = c, Reason = reason, Owner = owner, RequiresAdmin = NeedsAdmin(dir),
    };

    internal static LeftoverItem RegKey(string key, Confidence c, string reason, string owner) => new()
    {
        Kind = LeftoverKind.RegistryKey, Path = key, Display = key, Confidence = c, Reason = reason, Owner = owner,
        RequiresAdmin = key.StartsWith("HKLM", StringComparison.Ordinal),
    };

    internal static bool NeedsAdmin(string path) =>
        Locations.Profile.Length == 0 || !Locations.IsUnderOrEqual(path, Locations.Profile);

    internal static string RootName(RootKind k) => k switch
    {
        RootKind.ProgramFiles => "Programme",
        RootKind.ProgramData => "ProgramData",
        RootKind.Roaming => "AppData\\Roaming",
        RootKind.Local => "AppData\\Local",
        RootKind.LocalLow => "AppData\\LocalLow",
        RootKind.LocalPrograms => "AppData\\Local\\Programs",
        RootKind.Temp => "Temp",
        _ => "Startmenü",
    };

    internal static List<string> SafeDirs(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            }).ToList();
        }
        catch { return new List<string>(); }
    }

    internal static List<string> SafeSubKeys(Microsoft.Win32.RegistryKey k)
    {
        try { return k.GetSubKeyNames().ToList(); } catch { return new List<string>(); }
    }

    internal sealed record TaskInfo(string TaskPath, string File, string Command);

    private static readonly Regex s_command = new(@"<Command>\s*(.*?)\s*</Command>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Liest geplante Aufgaben direkt aus %windir%\System32\Tasks (vollständig nur mit Adminrechten).</summary>
    internal static List<TaskInfo> ReadTasks()
    {
        var list = new List<TaskInfo>();
        string root = Path.Combine(Locations.Windows, "System32", "Tasks");
        if (!Directory.Exists(root)) return list;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
        catch { return list; }
        foreach (var f in files)
        {
            try
            {
                string rel = f[root.Length..];
                if (rel.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                string xml = File.ReadAllText(f);
                var m = s_command.Match(xml);
                if (!m.Success) continue;
                string cmd = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim().Trim('"');
                cmd = AppCatalog.CleanPath(cmd);
                if (!cmd.Contains('\\')) continue;
                list.Add(new TaskInfo(rel, f, cmd));
            }
            catch { }
        }
        return list;
    }

    /// <summary>Firewall-Regeln mit Programmpfad (Wertname, Programmpfad).</summary>
    internal static List<(string Value, string App)> FirewallRules()
    {
        var list = new List<(string, string)>();
        using var k = RegPath.Open(FirewallKey);
        if (k == null) return list;
        try
        {
            foreach (var v in k.GetValueNames())
            {
                if (k.GetValue(v) is not string data) continue;
                int i = data.IndexOf("|App=", StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                int end = data.IndexOf('|', i + 5);
                string app = AppCatalog.CleanPath(end > 0 ? data[(i + 5)..end] : data[(i + 5)..]);
                if (app.Length > 3 && app.Contains('\\')) list.Add((v, app));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Entfernt Dubletten und Einträge, die bereits in einem ausgewählten Elternordner liegen; misst Größen.</summary>
    internal static List<LeftoverItem> Finish(List<LeftoverItem> items, CancellationToken ct)
    {
        var unique = new Dictionary<string, LeftoverItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (unique.TryGetValue(i.Key, out var existing))
            {
                if (i.Confidence < existing.Confidence) unique[i.Key] = i;
                continue;
            }
            unique[i.Key] = i;
        }

        var list = unique.Values.ToList();
        var folders = list.Where(i => i.Kind == LeftoverKind.Folder).Select(i => i.Path).ToList();
        var regKeys = list.Where(i => i.Kind is LeftoverKind.RegistryKey or LeftoverKind.UninstallEntry).Select(i => i.Path).ToList();
        list.RemoveAll(i =>
            (i.IsFileSystem && folders.Any(f => !string.Equals(f, i.Path, StringComparison.OrdinalIgnoreCase) && Locations.IsUnderOrEqual(i.Path, f))) ||
            (i.Kind is LeftoverKind.RegistryKey && regKeys.Any(r => !string.Equals(r, i.Path, StringComparison.OrdinalIgnoreCase) &&
                                                                      i.Path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))));

        Parallel.ForEach(list.Where(i => i.IsFileSystem), new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, i =>
        {
            try
            {
                if (i.Kind == LeftoverKind.Folder) i.Size = DirectorySize.Compute(i.Path, ct);
                else if (File.Exists(i.Path)) i.Size = new FileInfo(i.Path).Length;
            }
            catch { }
        });

        list.Sort((a, b) =>
        {
            int c = a.Confidence.CompareTo(b.Confidence);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            return c != 0 ? c : b.Size.CompareTo(a.Size);
        });
        return list;
    }
}
