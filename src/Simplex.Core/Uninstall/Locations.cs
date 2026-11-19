namespace Simplex.Core.Uninstall;

public enum RootKind
{
    ProgramFiles,
    ProgramData,
    Roaming,
    Local,
    LocalLow,
    LocalPrograms,
    Temp,
    StartMenu,
}

/// <summary>Bekannte Orte, an denen Programme Dateien hinterlassen.</summary>
public static class Locations
{
    private static string Env(Environment.SpecialFolder f)
    {
        try { return Environment.GetFolderPath(f).TrimEnd('\\'); } catch { return ""; }
    }

    public static string Windows { get; } = Env(Environment.SpecialFolder.Windows);
    public static string ProgramData { get; } = Env(Environment.SpecialFolder.CommonApplicationData);
    public static string Roaming { get; } = Env(Environment.SpecialFolder.ApplicationData);
    public static string Local { get; } = Env(Environment.SpecialFolder.LocalApplicationData);
    public static string Profile { get; } = Env(Environment.SpecialFolder.UserProfile);
    public static string LocalLow { get; } = Profile.Length > 0 ? Path.Combine(Profile, "AppData", "LocalLow") : "";
    public static string LocalPrograms { get; } = Local.Length > 0 ? Path.Combine(Local, "Programs") : "";
    public static string Temp { get; } = Path.GetTempPath().TrimEnd('\\');
    public static string Packages { get; } = Local.Length > 0 ? Path.Combine(Local, "Packages") : "";

    public static string[] StartMenus { get; } =
    {
        Env(Environment.SpecialFolder.Programs),
        Env(Environment.SpecialFolder.CommonPrograms),
    };

    public static string[] Desktops { get; } =
    {
        Env(Environment.SpecialFolder.DesktopDirectory),
        Env(Environment.SpecialFolder.CommonDesktopDirectory),
    };

    /// <summary>„Program Files“ und „Program Files (x86)“ auf allen festen Laufwerken.</summary>
    public static List<string> ProgramFilesDirs()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { Env(Environment.SpecialFolder.ProgramFiles), Env(Environment.SpecialFolder.ProgramFilesX86) })
            if (p.Length > 0 && Directory.Exists(p)) set.Add(p);
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                foreach (var name in new[] { "Program Files", "Program Files (x86)", "Programme", "Programs" })
                {
                    string p = Path.Combine(d.RootDirectory.FullName, name);
                    if (Directory.Exists(p)) set.Add(p);
                }
            }
        }
        catch { }
        return set.ToList();
    }

    public static IEnumerable<(string Path, RootKind Kind)> LeftoverRoots()
    {
        foreach (var p in ProgramFilesDirs()) yield return (p, RootKind.ProgramFiles);
        if (ProgramData.Length > 0) yield return (ProgramData, RootKind.ProgramData);
        if (Roaming.Length > 0) yield return (Roaming, RootKind.Roaming);
        if (LocalPrograms.Length > 0 && Directory.Exists(LocalPrograms)) yield return (LocalPrograms, RootKind.LocalPrograms);
        if (Local.Length > 0) yield return (Local, RootKind.Local);
        if (LocalLow.Length > 0 && Directory.Exists(LocalLow)) yield return (LocalLow, RootKind.LocalLow);
    }

    public static IEnumerable<string> ShortcutFiles()
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
        foreach (var root in StartMenus.Concat(Desktops))
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", opts).ToList(); } catch { continue; }
            foreach (var f in files) yield return f;
        }
    }

    public static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        return path.Length > 3 ? path.TrimEnd('\\', '/') : path;
    }

    /// <summary>Anzahl Ebenen unter dem Laufwerk: C:\ = 0, C:\Program Files = 1, C:\Program Files\Foo = 2.</summary>
    public static int Depth(string path)
    {
        path = Normalize(path);
        if (path.Length <= 3) return 0;
        return path.Count(c => c == '\\') - (path.StartsWith(@"\\", StringComparison.Ordinal) ? 3 : 0);
    }

    public static bool IsUnderOrEqual(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (path.Length == root.Length || path[root.Length] is '\\' or '/');

    public static bool IsWindowsDir(string path) => Windows.Length > 0 && IsUnderOrEqual(path, Windows);

    /// <summary>Ordner, die in den jeweiligen Wurzeln nie als Reste gelten.</summary>
    public static readonly HashSet<string> SystemFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Program Files
        "Common Files", "Internet Explorer", "Microsoft", "Microsoft.NET", "Microsoft Update Health Tools", "ModifiableWindowsApps", "MSBuild",
        "Reference Assemblies", "Uninstall Information", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows Mail",
        "Windows Media Player", "Windows Multimedia Platform", "Windows NT", "Windows Photo Viewer", "Windows Portable Devices",
        "Windows Security", "Windows Sidebar", "WindowsApps", "WindowsPowerShell", "dotnet", "Microsoft Office", "Microsoft Office 15",
        "Microsoft OneDrive", "Microsoft Visual Studio", "Microsoft SDKs", "Windows Kits", "Microsoft SQL Server", "IIS", "IIS Express",
        "Microsoft Analysis Services", "Microsoft Help Viewer", "Microsoft Silverlight", "Microsoft Synchronization Services",
        "Windows Identity Foundation", "Windows Journal", "Microsoft Games", "desktop.ini", "PCHealthCheck", "Microsoft Edge",
        "Microsoft Visual Studio 10.0", "Microsoft Visual Studio 14.0", "Microsoft Web Tools", "Microsoft Policy Platform", "rempl",
        "Windows App Certification Kit", "Application Verifier", "Microsoft Intune Management Extension", "Hyper-V", "Windows Sandbox",
        "WSL", "PowerShell", "Package Cache", "InstallShield Installation Information", "Temp",
        // ProgramData
        "Packages", "Package Cache", "regid.1991-06.com.microsoft", "ssh", "USOPrivate", "USOShared", "WindowsHolographicDevices",
        "Application Data", "Desktop", "Documents", "Favorites", "Start Menu", "Templates", "Comms", "SoftwareDistribution",
        "Microsoft OneDrive", "MicrosoftEdge", "Windows App Certification Kit", "WinGet", "chocolatey", "Intel", "NVIDIA", "NVIDIA Corporation",
        "AMD", "ATI", "Realtek", "Dolby", "Waves", "Synaptics", "Lenovo", "Dell", "HP", "ASUS", "Acer", "MSI", "Samsung", "regid.1991-06.com.microsoft",
        "Oracle", "Java", "boost_interprocess", "Docker", "DockerDesktop", "Sophos", "McAfee", "Norton", "Avast", "AVG", "Kaspersky",
        "Kaspersky Lab", "ESET", "Bitdefender", "Malwarebytes", "Mozilla-1de4eec8-1241-4177-a864-e594e8d1fb38",
        // AppData Roaming / Local / LocalLow
        "Microsoft", "Temp", "Programs", "ConnectedDevicesPlatform", "D3DSCache", "CrashDumps", "PeerDistRepub", "Publishers", "VirtualStore",
        "History", "Temporary Internet Files", "Microsoft_Corporation", "IsolatedStorage", "PlaceholderTileLogoFolder", "SquirrelTemp",
        "fontconfig", "pip", "npm", "npm-cache", "NuGet", ".NET", "dotnet", "Comms", "Sun", "Adobe", "Macromedia", "Google", "Mozilla", "Apple",
        "Apple Computer", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Packages", "assembly", "Downloaded Installations", "ElevatedDiagnostics",
        "GroupPolicy", "Diagnostics", "speech", "Steam", "cache", "Identities", "MicrosoftEdge", "OneDrive", "Teams", "SolidDocuments",
        "Application Data", "GDIPFONTCACHEV1.DAT", "IconCache.db", "Low", "Mozilla Firefox", "Unity", "Comms", "Package Cache",
        "Windows", "Windows Live", "Windows Mail", "Windows Media", "Windows Photo Viewer", "WindowsApps", "WinGet", "ToastNotificationManagerCompat",
        "CEF", "Electron", "Chromium", "Squirrel", "SimplexSpeichermanager", "Simplex", ".IdentityService", "PackageManagement", "PowerShell",
        "Python", "pypa", "virtualenv", "Yarn", "pnpm", "node-gyp", "go-build", "fontcache", "ms-playwright", "wsl", "lxss",
        "CrashReportClient", "Unity", "UnityCrashHandler", "GitCredentialManager", ".dotnet", ".nuget", "Mozilla-1de4eec8-1241-4177-a864-e594e8d1fb38",
    };
}
