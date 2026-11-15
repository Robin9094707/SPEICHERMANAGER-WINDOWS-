using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Simplex.Core.Cleanup;

namespace Simplex.Core.Games;

public sealed class GameInfo
{
    public required string Name { get; init; }
    public required string Launcher { get; init; }
    public required string InstallPath { get; init; }
    public long Size { get; set; }
    public bool SizeKnown { get; set; }
    /// <summary>URI oder Befehl zum Deinstallieren (z. B. steam://uninstall/123).</summary>
    public string? UninstallUri { get; init; }
    public string? UninstallCommand { get; init; }
}

/// <summary>Findet installierte Spiele von Steam, Epic, GOG, Ubisoft, EA, Battle.net, Riot, Rockstar und Xbox.</summary>
public static class GameDetector
{
    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        Try(() => Steam(games));
        Try(() => Epic(games));
        Try(() => Gog(games));
        Try(() => Ubisoft(games));
        Try(() => Xbox(games));
        Try(() => FromUninstallRegistry(games));

        // Doppelte (gleicher Installationsordner) entfernen – erster Treffer (genauester Launcher) gewinnt.
        var unique = new List<GameInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in games)
        {
            string key = g.InstallPath.TrimEnd('\\');
            if (Directory.Exists(key) && seen.Add(key)) unique.Add(g);
        }
        return unique;
    }

    private static void Try(Action a) { try { a(); } catch { } }

    private static string Vdf(string text, string key)
    {
        var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : "";
    }

    private static void Steam(List<GameInfo> games)
    {
        var steam = CleanupCatalog.SteamPath();
        if (steam == null) return;
        foreach (var lib in CleanupCatalog.Libraries(steam))
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    string text = File.ReadAllText(acf);
                    string appId = Vdf(text, "appid");
                    string name = Vdf(text, "name");
                    string dir = Vdf(text, "installdir");
                    if (appId == "228980" || string.IsNullOrEmpty(dir)) continue; // Steamworks Redistributables
                    long.TryParse(Vdf(text, "SizeOnDisk"), out long size);
                    games.Add(new GameInfo
                    {
                        Name = string.IsNullOrEmpty(name) ? dir : name,
                        Launcher = "Steam",
                        InstallPath = Path.Combine(apps, "common", dir),
                        Size = size,
                        SizeKnown = size > 0,
                        UninstallUri = "steam://uninstall/" + appId,
                    });
                }
                catch { }
            }
        }
    }

    private static void Epic(List<GameInfo> games)
    {
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string manifests = Path.Combine(pd, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var r = doc.RootElement;
                string name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? "" : "";
                string loc = r.TryGetProperty("InstallLocation", out var l) ? l.GetString() ?? "" : "";
                long size = r.TryGetProperty("InstallSize", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                if (string.IsNullOrEmpty(loc)) continue;
                games.Add(new GameInfo
                {
                    Name = string.IsNullOrEmpty(name) ? Path.GetFileName(loc) : name,
                    Launcher = "Epic Games",
                    InstallPath = loc.Replace('/', '\\'),
                    Size = size,
                    SizeKnown = size > 0,
                    UninstallUri = "com.epicgames.launcher://store/library",
                });
            }
            catch { }
        }
    }

    private static void Gog(List<GameInfo> games)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root == null) return;
        foreach (var id in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(id);
            if (k?.GetValue("path") is not string path) continue;
            games.Add(new GameInfo
            {
                Name = k.GetValue("gameName") as string ?? Path.GetFileName(path),
                Launcher = "GOG",
                InstallPath = path,
                UninstallCommand = k.GetValue("uninstallCommand") as string,
            });
        }
    }

    private static void Ubisoft(List<GameInfo> games)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (root == null) return;
        foreach (var id in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(id);
            if (k?.GetValue("InstallDir") is not string path) continue;
            path = path.Replace('/', '\\').TrimEnd('\\');
            games.Add(new GameInfo
            {
                Name = Path.GetFileName(path),
                Launcher = "Ubisoft Connect",
                InstallPath = path,
                UninstallUri = "uplay://uninstall/" + id,
            });
        }
    }

    private static void Xbox(List<GameInfo> games)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
            string xg = Path.Combine(d.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(xg)) continue;
            foreach (var g in Directory.GetDirectories(xg))
            {
                string name = Path.GetFileName(g);
                if (name.Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                games.Add(new GameInfo
                {
                    Name = name,
                    Launcher = "Xbox / Game Pass",
                    InstallPath = g,
                    UninstallUri = "ms-settings:appsfeatures",
                });
            }
        }
    }

    private static readonly (string Needle, string Launcher)[] s_publishers =
    {
        ("Electronic Arts", "EA app"),
        ("Blizzard", "Battle.net"),
        ("Riot Games", "Riot Games"),
        ("Rockstar Games", "Rockstar"),
        ("Bethesda", "Bethesda"),
        ("Ubisoft", "Ubisoft Connect"),
        ("Mojang", "Minecraft"),
        ("Valve", "Steam"),
        ("Epic Games", "Epic Games"),
        ("Battlestate Games", "Battlestate"),
        ("Wargaming", "Wargaming"),
        ("HoYoverse", "HoYoPlay"),
        ("miHoYo", "HoYoPlay"),
    };

    private static void FromUninstallRegistry(List<GameInfo> games)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, view);
                using var root = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root == null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k == null) continue;
                    string name = k.GetValue("DisplayName") as string ?? "";
                    string pub = k.GetValue("Publisher") as string ?? "";
                    string loc = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"');
                    if (name.Length == 0 || loc.Length < 4) continue;

                    string? launcher = null;
                    if (sub.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) launcher = "Steam";
                    else foreach (var (needle, l) in s_publishers)
                        if (pub.Contains(needle, StringComparison.OrdinalIgnoreCase)) { launcher = l; break; }
                    if (launcher == null) continue;
                    // Launcher selbst nicht als Spiel aufführen
                    if (Regex.IsMatch(name, @"\b(Launcher|App|Battle\.net|Client|Redistributable|Connect)\b", RegexOptions.IgnoreCase) &&
                        !sub.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) continue;

                    long size = k.GetValue("EstimatedSize") is int kb ? (long)(uint)kb * 1024 : 0;
                    games.Add(new GameInfo
                    {
                        Name = name,
                        Launcher = launcher,
                        InstallPath = loc,
                        Size = size,
                        SizeKnown = false, // Registry-Werte sind oft veraltet → nachmessen
                        UninstallCommand = k.GetValue("UninstallString") as string,
                    });
                }
            }
            catch { }
        }
    }
}
