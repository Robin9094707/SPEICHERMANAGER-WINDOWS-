using Microsoft.Win32;

namespace Simplex.Core.Cleanup;

/// <summary>Alle bekannten Cache-/Müll-Orte. Nur Inhalte, die Programme automatisch neu erzeugen.</summary>
public static class CleanupCatalog
{
    public const string GroupSystem = "Windows";
    public const string GroupBrowser = "Browser";
    public const string GroupApps = "Apps & Spiele";
    public const string GroupDev = "Entwickler";

    public static List<CleanupCategory> Build()
    {
        string local = F(Environment.SpecialFolder.LocalApplicationData);
        string roaming = F(Environment.SpecialFolder.ApplicationData);
        string profile = F(Environment.SpecialFolder.UserProfile);
        string win = F(Environment.SpecialFolder.Windows);
        string pd = F(Environment.SpecialFolder.CommonApplicationData);
        string localLow = Path.Combine(profile, "AppData", "LocalLow");
        string temp = Path.GetTempPath().TrimEnd('\\', '/');
        string sysDrive = Path.GetPathRoot(win) ?? @"C:\";

        var list = new List<CleanupCategory>
        {
            // ---------------- Windows ----------------
            new()
            {
                Id = "recycle", Group = GroupSystem, Name = "Papierkorb", Glyph = "\uE74D",
                Description = "Gelöschte Dateien in den Papierkörben aller Laufwerke endgültig entfernen.",
                Kind = CleanupKind.RecycleBin,
            },
            new()
            {
                Id = "user-temp", Group = GroupSystem, Name = "Temporäre Dateien", Glyph = "\uE7C3",
                Description = "Ihr Temp-Ordner. Dateien, die gerade benutzt werden oder jünger als 2 Stunden sind, bleiben erhalten.",
                Targets = new[] { new CleanupTarget(temp, MinAgeHours: 2) },
            },
            new()
            {
                Id = "win-temp", Group = GroupSystem, Name = "Temporäre Windows-Dateien", Glyph = "\uE7C3", RequiresAdmin = true,
                Description = "Systemweiter Temp-Ordner von Windows (C:\\Windows\\Temp).",
                Targets = new[] { new CleanupTarget(Path.Combine(win, "Temp"), MinAgeHours: 2) },
            },
            new()
            {
                Id = "thumbs", Group = GroupSystem, Name = "Miniaturansichten & Symbol-Cache", Glyph = "\uEB9F",
                Description = "Vorschaubilder im Explorer – werden bei Bedarf automatisch neu erstellt.",
                Targets = new[]
                {
                    new CleanupTarget(Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false),
                    new CleanupTarget(Path.Combine(local, "Microsoft", "Windows", "Explorer"), "iconcache_*.db", false),
                },
            },
            new()
            {
                Id = "inetcache", Group = GroupSystem, Name = "Internet-Cache von Windows", Glyph = "\uE774",
                Description = "Zwischengespeicherte Webinhalte von Windows, Office und älteren Apps.",
                Targets = new[] { new CleanupTarget(Path.Combine(local, "Microsoft", "Windows", "INetCache")) },
            },
            new()
            {
                Id = "user-dumps", Group = GroupSystem, Name = "Fehlerberichte & Absturzabbilder", Glyph = "\uE7BA",
                Description = "Windows-Fehlerberichterstattung und Crash-Dumps von Programmen.",
                Targets = new[]
                {
                    new CleanupTarget(Path.Combine(local, "Microsoft", "Windows", "WER")),
                    new CleanupTarget(Path.Combine(local, "CrashDumps")),
                },
            },
            new()
            {
                Id = "winupdate", Group = GroupSystem, Name = "Windows Update-Cache", Glyph = "\uE895", RequiresAdmin = true,
                Description = "Bereits installierte Update-Downloads. Werden bei Bedarf erneut geladen.",
                Targets = new[] { new CleanupTarget(Path.Combine(win, "SoftwareDistribution", "Download")) },
            },
            new()
            {
                Id = "delivery", Group = GroupSystem, Name = "Übermittlungsoptimierung", Glyph = "\uE753", RequiresAdmin = true,
                Description = "Cache, mit dem Windows Updates im Netzwerk teilt.",
                Targets = new[] { new CleanupTarget(Path.Combine(win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")) },
            },
            new()
            {
                Id = "sys-logs", Group = GroupSystem, Name = "Systemprotokolle & System-Dumps", Glyph = "\uE9F9", RequiresAdmin = true,
                Description = "CBS/DISM-Protokolle, systemweite Fehlerberichte, Minidumps und MEMORY.DMP.",
                Targets = new[]
                {
                    new CleanupTarget(Path.Combine(win, "Logs", "CBS"), "*.log"),
                    new CleanupTarget(Path.Combine(win, "Logs", "CBS"), "*.cab"),
                    new CleanupTarget(Path.Combine(win, "Logs", "DISM"), "*.log"),
                    new CleanupTarget(Path.Combine(win, "Logs", "WindowsUpdate"), "*.etl"),
                    new CleanupTarget(Path.Combine(win, "Minidump")),
                    new CleanupTarget(win, "MEMORY.DMP", false),
                    new CleanupTarget(Path.Combine(win, "LiveKernelReports"), "*.dmp"),
                    new CleanupTarget(Path.Combine(pd, "Microsoft", "Windows", "WER")),
                },
            },
            new()
            {
                Id = "shader", Group = GroupSystem, Name = "Grafik-Shader-Cache", Glyph = "\uE7F4",
                Description = "DirectX-, NVIDIA-, AMD- und Intel-Shader-Caches. Spiele erzeugen sie beim nächsten Start neu.",
                Targets = new[]
                {
                    new CleanupTarget(Path.Combine(local, "D3DSCache")),
                    new CleanupTarget(Path.Combine(local, "NVIDIA", "DXCache")),
                    new CleanupTarget(Path.Combine(local, "NVIDIA", "GLCache")),
                    new CleanupTarget(Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "DXCache")),
                    new CleanupTarget(Path.Combine(local, "AMD", "DxCache")),
                    new CleanupTarget(Path.Combine(local, "AMD", "DxcCache")),
                    new CleanupTarget(Path.Combine(local, "AMD", "GLCache")),
                    new CleanupTarget(Path.Combine(local, "AMD", "VkCache")),
                    new CleanupTarget(Path.Combine(localLow, "Intel", "ShaderCache")),
                    new CleanupTarget(Path.Combine(local, "Intel", "ShaderCache")),
                },
            },
            new()
            {
                Id = "driver-leftovers", Group = GroupSystem, Name = "Treiber-Installationsreste", Glyph = "\uE964",
                Description = "Entpackte Treiberpakete von AMD/NVIDIA (z. B. C:\\AMD), die nach der Installation nicht mehr gebraucht werden.",
                Targets = new[]
                {
                    new CleanupTarget(Path.Combine(sysDrive, "AMD")),
                    new CleanupTarget(Path.Combine(sysDrive, "NVIDIA")),
                    new CleanupTarget(Path.Combine(pd, "NVIDIA Corporation", "Downloader")),
                },
            },
        };

        // ---------------- Browser ----------------
        AddChromium(list, "chrome", "Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data"));
        AddChromium(list, "edge", "Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"));
        AddChromium(list, "brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
        AddChromium(list, "vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data"));
        AddChromium(list, "opera", "Opera", Path.Combine(local, "Opera Software", "Opera Stable"), Path.Combine(roaming, "Opera Software", "Opera Stable"));
        AddChromium(list, "operagx", "Opera GX", Path.Combine(local, "Opera Software", "Opera GX Stable"), Path.Combine(roaming, "Opera Software", "Opera GX Stable"));

        var ffTargets = new List<CleanupTarget>();
        foreach (var prof in SubDirs(Path.Combine(local, "Mozilla", "Firefox", "Profiles")))
        {
            ffTargets.Add(new CleanupTarget(Path.Combine(prof, "cache2")));
            ffTargets.Add(new CleanupTarget(Path.Combine(prof, "startupCache")));
            ffTargets.Add(new CleanupTarget(Path.Combine(prof, "thumbnails")));
        }
        if (ffTargets.Count > 0)
            list.Add(new CleanupCategory
            {
                Id = "firefox", Group = GroupBrowser, Name = "Mozilla Firefox", Glyph = "\uE774",
                Description = "Browser-Cache. Verlauf, Passwörter und Cookies bleiben erhalten.",
                Targets = ffTargets,
            });

        // ---------------- Apps & Spiele ----------------
        AddElectron(list, "discord", "Discord", Path.Combine(roaming, "discord"));
        AddElectron(list, "slack", "Slack", Path.Combine(roaming, "Slack"));
        AddElectron(list, "teams", "Microsoft Teams (klassisch)", Path.Combine(roaming, "Microsoft", "Teams"));
        AddElectron(list, "vscode", "Visual Studio Code", Path.Combine(roaming, "Code"), "CachedData", "CachedExtensionVSIXs", "logs");

        AddIfAny(list, new CleanupCategory
        {
            Id = "spotify", Group = GroupApps, Name = "Spotify", Glyph = "\uEC4F",
            Description = "Zwischengespeicherte Songs. Offline-Downloads bleiben erhalten.",
            Targets = new[]
            {
                new CleanupTarget(Path.Combine(local, "Spotify", "Data")),
                new CleanupTarget(Path.Combine(local, "Spotify", "Browser", "Cache")),
            },
        });

        string? steam = SteamPath();
        var steamTargets = new List<CleanupTarget> { new(Path.Combine(local, "Steam", "htmlcache")) };
        if (steam != null)
        {
            steamTargets.Add(new CleanupTarget(Path.Combine(steam, "appcache", "httpcache")));
            steamTargets.Add(new CleanupTarget(Path.Combine(steam, "logs")));
            steamTargets.Add(new CleanupTarget(Path.Combine(steam, "dumps")));
            steamTargets.Add(new CleanupTarget(Path.Combine(steam, "depotcache")));
        }
        AddIfAny(list, new CleanupCategory
        {
            Id = "steam", Group = GroupApps, Name = "Steam (Web-Cache & Protokolle)", Glyph = "\uE7FC",
            Description = "Browser-Cache des Steam-Clients, Logs und Absturzberichte. Spiele bleiben unberührt.",
            Targets = steamTargets,
        });
        if (steam != null)
        {
            var shaderTargets = Libraries(steam).Select(l => new CleanupTarget(Path.Combine(l, "steamapps", "shadercache"))).ToList();
            AddIfAny(list, new CleanupCategory
            {
                Id = "steam-shader", Group = GroupApps, Name = "Steam-Shader-Cache", Glyph = "\uE7FC", DefaultSelected = false,
                Description = "Vorkompilierte Shader aller Spiele. Werden neu geladen – erster Spielstart kann kurz ruckeln.",
                Targets = shaderTargets,
            });
        }
        AddIfAny(list, new CleanupCategory
        {
            Id = "epic", Group = GroupApps, Name = "Epic Games Launcher", Glyph = "\uE7FC",
            Description = "Web-Cache und Protokolle des Launchers.",
            Targets = new[]
            {
                new CleanupTarget(Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache")),
                new CleanupTarget(Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache_4147")),
                new CleanupTarget(Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache_4430")),
                new CleanupTarget(Path.Combine(local, "EpicGamesLauncher", "Saved", "Logs")),
            },
        });

        // ---------------- Entwickler ----------------
        AddIfAny(list, Dev("npm", "npm-Cache", Path.Combine(local, "npm-cache")));
        AddIfAny(list, Dev("yarn", "Yarn-Cache", Path.Combine(local, "Yarn", "Cache")));
        AddIfAny(list, Dev("pnpm", "pnpm-Store", Path.Combine(local, "pnpm", "store")));
        AddIfAny(list, Dev("pip", "pip-Cache (Python)", Path.Combine(local, "pip", "Cache")));
        AddIfAny(list, Dev("nuget-http", "NuGet HTTP-Cache", Path.Combine(local, "NuGet", "v3-cache"), Path.Combine(local, "NuGet", "plugins-cache")));
        AddIfAny(list, Dev("nuget-pkgs", "NuGet-Pakete (global)", Path.Combine(profile, ".nuget", "packages")));
        AddIfAny(list, Dev("gradle", "Gradle-Cache", Path.Combine(profile, ".gradle", "caches")));
        AddIfAny(list, Dev("cargo", "Cargo-Registry-Cache (Rust)", Path.Combine(profile, ".cargo", "registry", "cache")));
        AddIfAny(list, Dev("gobuild", "Go-Build-Cache", Path.Combine(local, "go-build")));

        return list;
    }

    private static CleanupCategory Dev(string id, string name, params string[] dirs) => new()
    {
        Id = id, Group = GroupDev, Name = name, Glyph = "\uE943", DefaultSelected = false,
        Description = "Paket-Cache – wird beim nächsten Build bei Bedarf neu heruntergeladen.",
        Targets = dirs.Select(d => new CleanupTarget(d)).ToArray(),
    };

    private static void AddIfAny(List<CleanupCategory> list, CleanupCategory c)
    {
        if (c.Targets.Any(t => Directory.Exists(t.Directory))) list.Add(c);
    }

    private static void AddChromium(List<CleanupCategory> list, string id, string name, params string[] userDataDirs)
    {
        var targets = new List<CleanupTarget>();
        foreach (var userData in userDataDirs.Where(Directory.Exists))
        {
            foreach (var shared in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache", "component_crx_cache", "Crashpad\\reports" })
                targets.Add(new CleanupTarget(Path.Combine(userData, shared)));

            var profiles = new List<string>();
            if (File.Exists(Path.Combine(userData, "Preferences"))) profiles.Add(userData);
            profiles.AddRange(SubDirs(userData).Where(d => File.Exists(Path.Combine(d, "Preferences"))));

            foreach (var p in profiles)
            {
                targets.Add(new CleanupTarget(Path.Combine(p, "Cache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "Code Cache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "GPUCache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "DawnCache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "DawnGraphiteCache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "DawnWebGPUCache")));
                targets.Add(new CleanupTarget(Path.Combine(p, "Service Worker", "CacheStorage")));
                targets.Add(new CleanupTarget(Path.Combine(p, "Service Worker", "ScriptCache")));
            }
        }
        // Opera legt den Cache unter Local ab, die Profile unter Roaming.
        if (targets.Count == 0) return;
        list.Add(new CleanupCategory
        {
            Id = id, Group = GroupBrowser, Name = name, Glyph = "\uE774",
            Description = "Browser-Cache. Verlauf, Passwörter, Cookies und Tabs bleiben erhalten. Browser vorher schließen für bestes Ergebnis.",
            Targets = targets,
        });
    }

    private static void AddElectron(List<CleanupCategory> list, string id, string name, string baseDir, params string[] extra)
    {
        if (!Directory.Exists(baseDir)) return;
        var dirs = new List<string> { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "Service Worker\\CacheStorage", "Crashpad\\reports" };
        dirs.AddRange(extra);
        list.Add(new CleanupCategory
        {
            Id = id, Group = GroupApps, Name = name, Glyph = "\uE8F2",
            Description = "Zwischenspeicher der App. Anmeldung und Einstellungen bleiben erhalten.",
            Targets = dirs.Select(d => new CleanupTarget(Path.Combine(baseDir, d))).ToArray(),
        });
    }

    internal static IEnumerable<string> SubDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string F(Environment.SpecialFolder f)
    {
        try { return Environment.GetFolderPath(f); } catch { return ""; }
    }

    public static string? SteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p)
            {
                p = p.Replace('/', '\\');
                if (Directory.Exists(p)) return p;
            }
        }
        catch { }
        foreach (var guess in new[]
        {
            Path.Combine(F(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(F(Environment.SpecialFolder.ProgramFiles), "Steam"),
        })
            if (Directory.Exists(guess)) return guess;
        return null;
    }

    /// <summary>Liest alle Steam-Bibliotheksordner aus libraryfolders.vdf.</summary>
    public static List<string> Libraries(string steamPath)
    {
        var libs = new List<string> { steamPath };
        try
        {
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
            }
        }
        catch { }
        return libs;
    }
}
