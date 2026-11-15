namespace Simplex.Core.Safety;

public enum SafetyLevel
{
    /// <summary>Kann bedenkenlos gelöscht werden (persönliche Datei, Download, Video …).</summary>
    Safe,
    /// <summary>Löschbar, aber mit Folgen (gehört zu Programm/Spiel, App-Daten, VM-Festplatte …).</summary>
    Caution,
    /// <summary>Systemwichtig – wird niemals gelöscht.</summary>
    Protected,
}

public readonly record struct SafetyVerdict(SafetyLevel Level, string Reason)
{
    public static readonly SafetyVerdict SafeVerdict = new(SafetyLevel.Safe, "");
}

/// <summary>
/// Entscheidet, ob eine Datei oder ein Ordner gelöscht werden darf.
/// Alles unter Windows, Bootdateien, Auslagerungs-/Ruhezustandsdateien, Registry-Hives,
/// Systemdateien (System-Attribut) und Schlüsselordner des Systems sind tabu.
/// </summary>
public static class SafetyGuard
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private static readonly List<(string Path, string Reason)> s_protectedTrees = new();
    private static readonly List<(string Path, string Reason)> s_cautionTrees = new();
    private static readonly HashSet<string> s_protectedFolders = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> s_protectedRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT", "BOOTSECT.BAK",
        "DumpStack.log", "DumpStack.log.tmp", "boot.ini", "ntldr", "io.sys", "msdos.sys",
    };

    private static readonly string[] s_hiveNames = { "NTUSER.DAT", "UsrClass.dat" };

    static SafetyGuard()
    {
        string win = Env(Environment.SpecialFolder.Windows);
        string pf = Env(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Env(Environment.SpecialFolder.ProgramFilesX86);
        string pd = Env(Environment.SpecialFolder.CommonApplicationData);
        string profile = Env(Environment.SpecialFolder.UserProfile);
        string local = Env(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Env(Environment.SpecialFolder.ApplicationData);
        string users = string.IsNullOrEmpty(profile) ? "" : Path.GetDirectoryName(profile) ?? "";

        AddProtected(win, "Windows-Systemordner");

        foreach (var drive in SafeDrives())
        {
            string d = drive.TrimEnd('\\');
            AddProtected(d + @"\System Volume Information", "Systemwiederherstellung / Volume-Daten");
            AddProtected(d + @"\$Recycle.Bin", "Papierkorb – bitte über die Bereinigung leeren");
            AddProtected(d + @"\Recovery", "Windows-Wiederherstellungsumgebung");
            AddProtected(d + @"\Boot", "Bootdateien");
            AddProtected(d + @"\EFI", "Bootdateien (EFI)");
            AddProtected(d + @"\$WinREAgent", "Windows-Update-Wiederherstellung");
            AddProtected(d + @"\Config.Msi", "Laufende Installationen");
            AddProtected(d + @"\Documents and Settings", "Systemverknüpfung");
            AddProtected(d + @"\$Extend", "NTFS-Metadaten");
            s_protectedFolders.Add(d + "\\");
            s_protectedFolders.Add(d);
        }

        foreach (var p in new[] { pf, pf86 })
        {
            if (string.IsNullOrEmpty(p)) continue;
            AddProtected(Path.Combine(p, "WindowsApps"), "Microsoft-Store-Apps (systemverwaltet)");
            AddProtected(Path.Combine(p, "ModifiableWindowsApps"), "Microsoft-Store-Spiele (systemverwaltet)");
            AddProtected(Path.Combine(p, "Windows Defender"), "Windows-Sicherheit");
            AddProtected(Path.Combine(p, "Windows Defender Advanced Threat Protection"), "Windows-Sicherheit");
            AddProtected(Path.Combine(p, "Windows Security"), "Windows-Sicherheit");
            AddProtected(Path.Combine(p, "Windows NT"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Windows Mail"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Windows Media Player"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Windows Photo Viewer"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Windows Sidebar"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Internet Explorer"), "Windows-Komponente");
            AddProtected(Path.Combine(p, "Common Files"), "Gemeinsame Programmkomponenten");
            AddProtected(Path.Combine(p, "Microsoft Update Health Tools"), "Windows-Update");
            AddCaution(p, "Gehört zu einem installierten Programm – besser regulär deinstallieren");
            s_protectedFolders.Add(p);
        }

        if (!string.IsNullOrEmpty(pd))
        {
            AddProtected(Path.Combine(pd, "Microsoft", "Windows Defender"), "Windows-Sicherheit");
            AddProtected(Path.Combine(pd, "Microsoft", "Crypto"), "Schlüssel & Zertifikate");
            AddProtected(Path.Combine(pd, "Microsoft", "Windows", "Start Menu"), "Startmenü");
            AddProtected(Path.Combine(pd, "Microsoft", "Windows", "AppRepository"), "App-Registrierung");
            AddProtected(Path.Combine(pd, "Packages"), "Store-App-Daten (systemweit)");
            AddCaution(Path.Combine(pd, "Package Cache"), "Installations-Cache – wird zum Reparieren/Deinstallieren benötigt");
            AddCaution(pd, "Programmdaten – wird ggf. noch von Programmen benötigt");
            s_protectedFolders.Add(pd);
            s_protectedFolders.Add(Path.Combine(pd, "Microsoft"));
        }

        if (!string.IsNullOrEmpty(users))
        {
            s_protectedFolders.Add(users);
            AddProtected(Path.Combine(users, "Default"), "Standard-Benutzerprofil");
            AddProtected(Path.Combine(users, "All Users"), "Systemverknüpfung");
            AddProtected(Path.Combine(users, "Default User"), "Systemverknüpfung");
        }

        if (!string.IsNullOrEmpty(local))
        {
            AddProtected(Path.Combine(local, "Microsoft", "WindowsApps"), "App-Ausführungsaliase");
            AddProtected(Path.Combine(local, "Microsoft", "Credentials"), "Gespeicherte Anmeldedaten");
            AddProtected(Path.Combine(local, "Microsoft", "Windows", "UsrClass.dat"), "Registry");
            AddCaution(Path.Combine(local, "Packages"), "Store-App-Daten – App verliert ggf. Daten");
            AddCaution(Path.Combine(local, "Docker"), "Docker-Daten – Container/Images gehen verloren");
            AddCaution(local, "Anwendungsdaten – Programm verliert ggf. Daten/Einstellungen");
        }
        if (!string.IsNullOrEmpty(roaming))
        {
            AddProtected(Path.Combine(roaming, "Microsoft", "Protect"), "Verschlüsselungsschlüssel (DPAPI)");
            AddProtected(Path.Combine(roaming, "Microsoft", "Credentials"), "Gespeicherte Anmeldedaten");
            AddProtected(Path.Combine(roaming, "Microsoft", "SystemCertificates"), "Zertifikate");
            AddCaution(roaming, "Anwendungsdaten – Programm verliert ggf. Daten/Einstellungen");
        }

        if (!string.IsNullOrEmpty(profile))
        {
            s_protectedFolders.Add(profile);
            foreach (var sf in new[]
            {
                Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
            })
            {
                var f = Env(sf);
                if (!string.IsNullOrEmpty(f)) s_protectedFolders.Add(f);
            }
            s_protectedFolders.Add(Path.Combine(profile, "Downloads"));
            s_protectedFolders.Add(Path.Combine(profile, "AppData"));
            s_protectedFolders.Add(Path.Combine(profile, "AppData", "LocalLow"));
            s_protectedFolders.Add(Path.Combine(profile, "OneDrive"));
        }

        // Längste Pfade zuerst → spezifischste Regel gewinnt.
        s_protectedTrees.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length));
        s_cautionTrees.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length));
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { yield break; }
        foreach (var d in drives)
        {
            if (d.DriveType is DriveType.Fixed or DriveType.Removable)
                yield return d.Name;
        }
    }

    private static string Env(Environment.SpecialFolder f)
    {
        try { return Environment.GetFolderPath(f).TrimEnd('\\', '/'); } catch { return ""; }
    }

    private static void AddProtected(string path, string reason)
    {
        if (!string.IsNullOrWhiteSpace(path)) s_protectedTrees.Add((path.TrimEnd('\\', '/'), reason));
    }

    private static void AddCaution(string path, string reason)
    {
        if (!string.IsNullOrWhiteSpace(path)) s_cautionTrees.Add((path.TrimEnd('\\', '/'), reason));
    }

    private static bool IsUnderOrEqual(string path, string root) =>
        path.StartsWith(root, Cmp) && (path.Length == root.Length || path[root.Length] is '\\' or '/');

    private static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        return path.Length > 3 ? path.TrimEnd('\\', '/') : path;
    }

    public static SafetyVerdict CheckFile(string path, FileAttributes attributes)
    {
        path = Normalize(path);
        string name = Path.GetFileName(path);
        string? dir = Path.GetDirectoryName(path);

        foreach (var (root, reason) in s_protectedTrees)
            if (IsUnderOrEqual(path, root)) return new(SafetyLevel.Protected, reason);

        if (dir != null && Path.GetPathRoot(path) is { } pr &&
            string.Equals(dir.TrimEnd('\\'), pr.TrimEnd('\\'), Cmp) && s_protectedRootFiles.Contains(name))
            return new(SafetyLevel.Protected, name.ToLowerInvariant() switch
            {
                "pagefile.sys" or "swapfile.sys" => "Auslagerungsdatei – wird von Windows verwaltet",
                "hiberfil.sys" => "Ruhezustandsdatei – über „Ruhezustand deaktivieren“ entfernbar",
                _ => "Bootdatei",
            });

        foreach (var hive in s_hiveNames)
            if (name.StartsWith(hive, Cmp)) return new(SafetyLevel.Protected, "Registry-Datei des Benutzers");

        if ((attributes & FileAttributes.System) != 0)
            return new(SafetyLevel.Protected, "Als Systemdatei markiert");

        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is ".vhd" or ".vhdx" or ".avhdx" or ".vmdk" or ".vdi" or ".qcow2")
            return new(SafetyLevel.Caution, "Virtuelle Festplatte (WSL, Docker, Hyper-V oder VM) – Inhalt geht verloren");
        if (ext is ".pst" or ".ost")
            return new(SafetyLevel.Caution, "Outlook-Postfach-Datei");

        if (path.Contains(@"\steamapps\", Cmp))
            return new(SafetyLevel.Caution, "Spieldatei (Steam) – besser über Steam deinstallieren");
        if (path.Contains(@"\Epic Games\", Cmp) || path.Contains(@"\XboxGames\", Cmp))
            return new(SafetyLevel.Caution, "Spieldatei – besser über den Launcher deinstallieren");

        foreach (var (root, reason) in s_cautionTrees)
            if (IsUnderOrEqual(path, root)) return new(SafetyLevel.Caution, reason);

        return SafetyVerdict.SafeVerdict;
    }

    public static SafetyVerdict CheckDirectory(string path)
    {
        path = Normalize(path);
        if (s_protectedFolders.Contains(path) || path.Length <= 3)
            return new(SafetyLevel.Protected, "Wichtiger Systemordner – nur Inhalte können gelöscht werden");

        foreach (var (root, reason) in s_protectedTrees)
        {
            if (IsUnderOrEqual(path, root)) return new(SafetyLevel.Protected, reason);
            if (IsUnderOrEqual(root, path)) return new(SafetyLevel.Protected, "Enthält geschützte Systemdaten (" + reason + ")");
        }

        if (path.Contains(@"\steamapps\", Cmp) || path.Contains(@"\Epic Games\", Cmp) || path.Contains(@"\XboxGames\", Cmp))
            return new(SafetyLevel.Caution, "Spielordner – besser über den Launcher deinstallieren");

        foreach (var (root, reason) in s_cautionTrees)
            if (IsUnderOrEqual(path, root)) return new(SafetyLevel.Caution, reason);

        return SafetyVerdict.SafeVerdict;
    }
}
