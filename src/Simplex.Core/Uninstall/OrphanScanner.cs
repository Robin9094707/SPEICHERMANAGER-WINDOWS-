using System.Text.RegularExpressions;
using Simplex.Core.Native;
using Simplex.Core.Safety;

namespace Simplex.Core.Uninstall;

/// <summary>
/// Findet Reste von Programmen, die früher (auch mit anderen Tools oder unsauber) entfernt wurden:
/// verwaiste Ordner, Uninstall-Einträge ohne Programm, tote Verknüpfungen, App-Daten entfernter Store-Apps,
/// Dienste/Aufgaben/Autostarts/Firewall-Regeln, deren Programmdatei fehlt, und alte Registry-Schlüssel.
/// </summary>
public static class OrphanScanner
{
    public const string UnknownOwner = "Altlast";
    private static readonly Regex s_packageDir = new(@"^[^_]+_[a-z0-9]{13}$", RegexOptions.Compiled);
    private static readonly TimeSpan s_stale = TimeSpan.FromDays(180);

    private static readonly HashSet<string> s_registryWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Classes", "Clients", "Microsoft", "Policies", "RegisteredApplications", "WOW6432Node", "Wow6432Node", "ODBC", "Partner", "Intel",
        "AppDataLow", "Chromium", "IM Providers", "Netscape", "Description", "Hardware", "Setup", "DefaultUserEnvironment", "System",
        "Windows", "Khronos", "Licenses", "Mozilla", "MozillaPlugins", "Google", "Apple Inc.", "Apple Computer, Inc.", "Adobe", "Oracle",
        "JavaSoft", "Python", "Macromedia", "Realtek", "Dolby", "Waves", "Synaptics", "NVIDIA Corporation", "AMD", "ATI", "ATI Technologies",
        "Sysinternals", "Cygwin", "Unity Technologies", "Wacom", "Logitech", "Razer", "VMware, Inc.", "Docker Inc.", "OpenSSH", "Tcl", "GNU",
        "Qt", "Qt Project", "Trolltech", "Kitware", "LLVM", "Wine", "ej-technologies", "Install4j", "InstallShield", "Caphyon", "Nullsoft",
        "Inno Setup", "WinRAR", "7-Zip", "Igor Pavlov", "Simplex", "SimplexSpeichermanager", "Valve", "Epic Games", "EpicGames", "Steam",
        "Blizzard Entertainment", "Electronic Arts", "Ubisoft", "Riot Games", "Rockstar Games", "GOG.com", "Unity", "Unreal Engine",
        "Classes\\Local Settings", "BraveSoftware", "Opera Software", "Vivaldi", "Brave", "SyncEngines", "VideoLAN", "Igor Pavlov",
    };

    public static List<LeftoverItem> Scan(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Installierte Programme, Store-Apps, Dienste und Spiele werden erfasst…");
        var index = KnownSoftwareIndex.Build();
        var items = new List<LeftoverItem>();

        progress?.Report("Verwaiste Einträge in „Apps & Features“…");
        BrokenUninstallEntries(items);

        foreach (var (root, kind) in Locations.LeftoverRoots())
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Durchsuche " + root + "…");
            ScanFolders(root, kind, index, items);
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report("App-Daten entfernter Store-Apps…");
        OrphanPackages(index, items);

        progress?.Report("Tote Verknüpfungen in Startmenü und Desktop…");
        BrokenShortcuts(items);

        progress?.Report("Dienste, geplante Aufgaben, Autostart und Firewall…");
        BrokenServicesTasksRun(items);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Registry wird nach alten Programmschlüsseln durchsucht…");
        RegistryOrphans(index, items);

        progress?.Report("Größen werden berechnet…");
        return LeftoverScanner.Finish(items, ct);
    }

    private static void BrokenUninstallEntries(List<LeftoverItem> items)
    {
        foreach (var r in AppCatalog.ReadUninstallRecords())
        {
            if (r.WindowsInstaller || r.UninstallString.Length == 0) continue;
            if (r.KeyName.StartsWith("{", StringComparison.Ordinal) && r.UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) continue;
            if (UninstallRunner.UninstallerExists(r.UninstallString)) continue;
            if (r.InstallLocation.Length > 3 && Directory.Exists(r.InstallLocation)) continue;
            string icon = KnownSoftwareIndex.IconPath(r.DisplayIcon);
            if (icon.Length > 3 && File.Exists(icon)) continue;
            string name = r.Name.Length > 0 ? r.Name : r.KeyName;
            if (!RegPath.CanDeleteKey(r.RegistryPath)) continue;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.UninstallEntry, Path = r.RegistryPath, Display = name + "  (" + r.RegistryPath + ")",
                Confidence = Confidence.High, Reason = "Programm ist nicht mehr vorhanden – nur der Eintrag in „Apps & Features“ blieb übrig",
                Owner = name, RequiresAdmin = r.RegistryPath.StartsWith("HKLM", StringComparison.Ordinal),
            });
        }
    }

    private static void ScanFolders(string root, RootKind kind, KnownSoftwareIndex index, List<LeftoverItem> items)
    {
        foreach (var dir in LeftoverScanner.SafeDirs(root))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.') || name.StartsWith('{') || name.StartsWith('$')) continue;
            if (Locations.SystemFolderNames.Contains(name)) continue;
            if (kind == RootKind.Local && name is "Programs" or "Packages" or "Temp") continue;
            if (!NameKey.IsUsable(NameKey.Norm(name))) continue;
            if (index.IsPathInUse(dir)) continue;
            if (index.MatchName(name) > 0) continue;
            if (SafetyGuard.CheckDirectory(dir).Level == SafetyLevel.Protected) continue;

            var (files, hasBinaries, lastWrite) = Inspect(dir);
            bool stale = DateTime.Now - lastWrite > s_stale;
            string age = AgeText(lastWrite);
            Confidence c;
            string reason;
            if (files == 0)
            {
                c = Confidence.High;
                reason = "Leerer Ordner eines entfernten Programms";
            }
            else if (kind is RootKind.ProgramFiles or RootKind.LocalPrograms)
            {
                if (!hasBinaries) { c = Confidence.High; reason = "Keine Programmdateien mehr – nur Reste (Einstellungen/Logs)"; }
                else if (stale) { c = Confidence.Medium; reason = "Programmdateien ohne Installationseintrag, " + age; }
                else { c = Confidence.Low; reason = "Kein installiertes Programm gefunden – evtl. portable App, " + age; }
            }
            else
            {
                c = stale ? Confidence.Medium : Confidence.Low;
                reason = "Gehört zu keinem installierten Programm, " + age;
            }
            items.Add(LeftoverScanner.Folder(dir, c, reason + " (" + LeftoverScanner.RootName(kind) + ")", UnknownOwner));
        }
    }

    private static (int Files, bool HasBinaries, DateTime LastWrite) Inspect(string dir)
    {
        int files = 0;
        bool bin = false;
        DateTime last = DateTime.MinValue;
        try { last = Directory.GetLastWriteTime(dir); } catch { }
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                files++;
                string ext = Path.GetExtension(f);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) bin = true;
                if (files <= 400)
                {
                    try { var t = File.GetLastWriteTime(f); if (t > last) last = t; } catch { }
                }
                if (files > 5000) break;
            }
        }
        catch { files = Math.Max(files, 1); }
        return (files, bin, last);
    }

    private static string AgeText(DateTime last)
    {
        if (last == DateTime.MinValue) return "Alter unbekannt";
        var d = DateTime.Now - last;
        if (d.TotalDays >= 730) return $"seit {(int)(d.TotalDays / 365)} Jahren unverändert";
        if (d.TotalDays >= 60) return $"seit {(int)(d.TotalDays / 30)} Monaten unverändert";
        if (d.TotalDays >= 2) return $"zuletzt vor {(int)d.TotalDays} Tagen geändert";
        return "kürzlich geändert";
    }

    private static void OrphanPackages(KnownSoftwareIndex index, List<LeftoverItem> items)
    {
        if (index.PackageCount < 20 || Locations.Packages.Length == 0 || !Directory.Exists(Locations.Packages)) return;
        foreach (var dir in LeftoverScanner.SafeDirs(Locations.Packages))
        {
            string name = Path.GetFileName(dir);
            if (!s_packageDir.IsMatch(name)) continue;
            if (index.IsPackageInstalled(name)) continue;
            bool ms = name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
            items.Add(LeftoverScanner.Folder(dir, ms ? Confidence.Low : Confidence.Medium,
                "App-Daten einer nicht mehr installierten Store-App", name[..name.IndexOf('_')]));
        }
    }

    private static void BrokenShortcuts(List<LeftoverItem> items)
    {
        foreach (var lnk in Locations.ShortcutFiles())
        {
            string t = ShellLink.GetTarget(lnk);
            if (!IsCheckableLocalPath(t)) continue;
            if (File.Exists(t) || Directory.Exists(t)) continue;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.Shortcut, Path = lnk, Display = lnk, Confidence = Confidence.High,
                Reason = "Tote Verknüpfung – Ziel existiert nicht mehr: " + t, Owner = Path.GetFileNameWithoutExtension(lnk),
                RequiresAdmin = !lnk.StartsWith(Locations.Profile, StringComparison.OrdinalIgnoreCase),
            });
        }

        // Leere Startmenü-Ordner
        foreach (var root in Locations.StartMenus)
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            foreach (var d in LeftoverScanner.SafeDirs(root))
            {
                string name = Path.GetFileName(d);
                if (name is "Startup" or "Autostart" or "Administrative Tools" or "Windows Tools" or "Accessibility" or "Accessories" or
                    "Maintenance" or "System Tools" or "Windows PowerShell" or "Windows-Verwaltungsprogramme" or "Zubehör" or "Barrierefreiheit" or
                    "Windows-Systemprogramme" or "Windows-Zubehör" or "Windows Accessories" or "Windows System" or "Windows Administrative Tools" or
                    "Windows Ease of Access")
                    continue;
                bool empty;
                try { empty = !Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Any(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)); }
                catch { empty = false; }
                if (empty)
                    items.Add(LeftoverScanner.Folder(d, Confidence.High, "Leerer Startmenü-Ordner", name));
            }
        }
    }

    private static bool IsCheckableLocalPath(string t)
    {
        if (t.Length < 4 || t.Contains('%') || t.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (t.Length < 3 || t[1] != ':' || t[2] != '\\') return false;
        try
        {
            var di = new DriveInfo(t[..1]);
            return di.IsReady && di.DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    private static void BrokenServicesTasksRun(List<LeftoverItem> items)
    {
        foreach (var (name, image) in KnownSoftwareIndex.Services())
        {
            if (!IsCheckableLocalPath(image) || Locations.IsWindowsDir(image) || File.Exists(image)) continue;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.Service, Path = name, Display = name + " (" + image + ")", Confidence = Confidence.Medium,
                Reason = "Dienst verweist auf eine gelöschte Programmdatei", Owner = name, RequiresAdmin = true,
            });
        }

        foreach (var t in LeftoverScanner.ReadTasks())
        {
            if (!IsCheckableLocalPath(t.Command) || Locations.IsWindowsDir(t.Command) || File.Exists(t.Command)) continue;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.ScheduledTask, Path = t.TaskPath, Display = t.TaskPath + " → " + t.Command, Confidence = Confidence.Medium,
                Reason = "Geplante Aufgabe startet eine gelöschte Datei", Owner = Path.GetFileName(t.TaskPath), RequiresAdmin = true,
            });
        }

        foreach (var (key, value, data) in KnownSoftwareIndex.RunEntries())
        {
            string exe = KnownSoftwareIndex.ExtractExe(data);
            if (!IsCheckableLocalPath(exe) || File.Exists(exe)) continue;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.RegistryValue, Path = key, ValueName = value, Display = key + " → " + value, Confidence = Confidence.High,
                Reason = "Autostart verweist auf gelöschte Datei: " + exe, Owner = value, RequiresAdmin = key.StartsWith("HKLM", StringComparison.Ordinal),
            });
        }

        int fw = 0;
        foreach (var (value, app) in LeftoverScanner.FirewallRules())
        {
            if (!IsCheckableLocalPath(app) || Locations.IsWindowsDir(app) || File.Exists(app)) continue;
            if (++fw > 300) break;
            items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.FirewallRule,
                Path = @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules",
                ValueName = value, Display = "Firewall-Regel für " + app, Confidence = Confidence.Medium,
                Reason = "Firewall-Ausnahme für ein gelöschtes Programm", Owner = Path.GetFileNameWithoutExtension(app), RequiresAdmin = true,
            });
        }
    }

    private static void RegistryOrphans(KnownSoftwareIndex index, List<LeftoverItem> items)
    {
        foreach (var root in new[] { @"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node" })
        {
            using var k = RegPath.Open(root);
            if (k == null) continue;
            foreach (var sub in LeftoverScanner.SafeSubKeys(k))
            {
                if (s_registryWhitelist.Contains(sub) || sub.StartsWith("{", StringComparison.Ordinal)) continue;
                string full = RegPath.Combine(root, sub);
                if (!RegPath.CanDeleteKey(full)) continue;
                if (!NameKey.IsUsable(NameKey.Norm(sub))) continue;
                if (index.MatchName(sub) > 0) continue;
                if (ReferencesExistingFile(k, sub)) continue;
                items.Add(LeftoverScanner.RegKey(full, Confidence.Low,
                    "Registry-Schlüssel ohne zugehöriges installiertes Programm – bitte prüfen", sub));
            }
        }
    }

    /// <summary>Verweist ein Schlüssel (bis 2 Ebenen tief) auf eine existierende Datei/einen Ordner außerhalb von Windows? Dann ist er wohl noch in Gebrauch.</summary>
    private static bool ReferencesExistingFile(Microsoft.Win32.RegistryKey parent, string sub)
    {
        try
        {
            using var k = parent.OpenSubKey(sub);
            return k != null && Refs(k, 0);
        }
        catch { return true; }

        static bool Refs(Microsoft.Win32.RegistryKey key, int depth)
        {
            int n = 0;
            foreach (var v in key.GetValueNames())
            {
                if (++n > 60) break;
                if (key.GetValue(v) is not string s || s.Length < 4 || s[1] != ':' || s[2] != '\\') continue;
                string p = AppCatalog.CleanPath(KnownSoftwareIndex.ExtractExe(s) is { Length: > 0 } e ? e : s);
                if (Locations.IsWindowsDir(p)) continue;
                if (File.Exists(p) || Directory.Exists(p)) return true;
            }
            if (depth >= 2) return false;
            int c = 0;
            foreach (var name in key.GetSubKeyNames())
            {
                if (++c > 40) break;
                try { using var sk = key.OpenSubKey(name); if (sk != null && Refs(sk, depth + 1)) return true; } catch { }
            }
            return false;
        }
    }
}
