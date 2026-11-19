using System.Text;
using System.Text.RegularExpressions;

namespace Simplex.Core.Uninstall;

/// <summary>
/// Normalisiert Programm-, Hersteller- und Ordnernamen, damit „Mozilla Firefox (x64 de)“,
/// „Mozilla Firefox“ und der Ordner „Mozilla\Firefox“ einander zugeordnet werden können.
/// </summary>
public static class NameKey
{
    private static readonly Regex s_brackets = new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex s_version = new(@"(?<![a-z])v?\d+([._-]\d+)+[a-z]?\b|\b(19|20)\d\d\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_lang = new(@"\b[a-z]{2}-[a-z]{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_noise = new(
        @"\b(x64|x86|amd64|arm64|64[- ]?bit|32[- ]?bit|version|edition|update|setup|installer|deutsch|german|english|multilingual|remove only|nur entfernen|for windows|für windows|desktop|portable|build|release)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_corp = new(
        @"\b(inc|incorporated|corporation|corp|co|company|ltd|limited|llc|gmbh|ag|ab|sa|s\.a|srl|s\.r\.l|bv|b\.v|oy|as|a/s|kg|pty|plc|sarl|sas|spa|s\.p\.a|kk|group|international|software|technologies|technology|interactive|entertainment|studios|labs)\b\.?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Namen, die nie als Programmreste gelten (zu allgemein bzw. systemweit genutzt).</summary>
    public static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "microsoft", "microsoftcorporation", "windows", "windowsapps", "common", "commonfiles", "programs", "program", "programfiles",
        "packages", "package", "packagecache", "temp", "tmp", "cache", "caches", "data", "user", "users", "userdata", "public", "default",
        "system", "system32", "syswow64", "app", "apps", "application", "applications", "bin", "lib", "libs", "software", "games", "game",
        "tools", "tool", "utilities", "utils", "update", "updates", "updater", "setup", "installer", "install", "installs", "uninstall",
        "nvidia", "nvidiacorporation", "intel", "intelcorporation", "amd", "advancedmicrodevices", "ati", "realtek", "realteksemiconductor",
        "oem", "driver", "drivers", "logs", "log", "config", "settings", "local", "locallow", "roaming", "appdata", "programdata",
        "documents", "downloads", "desktop", "pictures", "music", "videos", "startmenu", "launcher", "client", "service", "services",
        "runtime", "runtimes", "shared", "sdk", "plugins", "plugin", "extensions", "x64", "x86", "win32", "win64", "current", "latest",
        "google", "apple", "adobe", "oracle", "mozilla", "khronos", "dolby", "waves", "conexant", "synaptics", "elan", "dell", "hp", "lenovo",
        "asus", "acer", "msi", "samsung", "sony", "logitech", "crashdumps", "crashreports", "d3dscache", "fonts", "icons", "assets",
        "resources", "help", "docs", "readme", "license", "licenses", "backup", "backups", "unknown", "new", "old", "test",
    };

    /// <summary>Exe-Namen, die keine Rückschlüsse auf das Programm erlauben.</summary>
    public static readonly HashSet<string> GenericExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "install", "installer", "uninstall", "uninst", "unins000", "unins001", "update", "updater", "launcher", "helper",
        "crashreporter", "crashpad_handler", "crashhandler", "bugreport", "elevate", "maintenancetool", "repair", "notification_helper",
        "service", "server", "client", "agent", "host", "app", "main", "start", "run", "cmd", "node", "python", "pythonw", "java", "javaw",
        "dotnet", "wscript", "cscript", "rundll32", "msiexec", "regsvr32", "7z", "7za", "curl", "wget", "git", "ffmpeg", "vc_redist.x64",
        "vc_redist.x86", "dxsetup", "createdump", "vcredist_x64", "vcredist_x86", "squirrel", "electron",
    };

    /// <summary>Nur Buchstaben/Ziffern, klein geschrieben.</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Entfernt Versionen, Architektur-, Sprach- und Füllwörter aus einem Programmnamen.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = name.Replace("®", "").Replace("™", "").Replace("©", "");
        s = s_brackets.Replace(s, " ");
        s = s_lang.Replace(s, " ");
        s = s_version.Replace(s, " ");
        s = s_noise.Replace(s, " ");
        s = Regex.Replace(s, @"\s+-\s*$|\s{2,}", " ").Trim(' ', '-', '_', '.', ',');
        return s;
    }

    /// <summary>„Valve Corporation“ → „Valve“, „Spotify AB“ → „Spotify“.</summary>
    public static string CleanPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return "";
        string s = publisher.Replace(",", " ").Replace("®", "").Replace("™", "");
        s = s_brackets.Replace(s, " ");
        s = s_corp.Replace(s, " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '.', '-');
    }

    /// <summary>Normalisierte Schlüssel zu einem Programmnamen (mit/ohne Herstellerpräfix).</summary>
    public static IEnumerable<string> KeysForName(string? name, string? publisher)
    {
        string clean = CleanName(name);
        string n = Norm(clean);
        if (IsUsable(n)) yield return n;

        string full = Norm(name);
        if (full != n && IsUsable(full)) yield return full;

        // Ohne Herstellerpräfix: „Mozilla Firefox“ → „Firefox“
        string pub = CleanPublisher(publisher);
        foreach (var p in new[] { pub, FirstWord(pub) })
        {
            if (p.Length < 3) continue;
            if (clean.StartsWith(p + " ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = Norm(clean[(p.Length + 1)..]);
                if (IsUsable(rest)) yield return rest;
            }
        }

        // Bei „Name: Untertitel“ bzw. „Name - Untertitel“ den Hauptnamen
        int cut = clean.IndexOfAny(new[] { ':', '–', '|' });
        if (cut < 0) { int dash = clean.IndexOf(" - ", StringComparison.Ordinal); if (dash > 0) cut = dash; }
        if (cut > 2)
        {
            string head = Norm(clean[..cut]);
            if (IsUsable(head)) yield return head;
        }
    }

    public static string PublisherKey(string? publisher)
    {
        string p = Norm(CleanPublisher(publisher));
        return IsUsable(p) ? p : "";
    }

    public static bool IsUsable(string norm) => norm.Length >= 3 && !Generic.Contains(norm) && !norm.All(char.IsDigit);

    private static string FirstWord(string s)
    {
        int i = s.IndexOf(' ');
        return i > 0 ? s[..i] : s;
    }

    /// <summary>
    /// Vergleicht einen normalisierten Ordnernamen mit einem Schlüssel.
    /// Exakt = 2, Präfix (mind. 5 Zeichen, kein bloßes Wortfragment) = 1, sonst 0.
    /// </summary>
    public static int Match(string folderNorm, string key)
    {
        if (folderNorm.Length == 0 || key.Length == 0) return 0;
        if (folderNorm == key) return 2;
        if (folderNorm.Length >= 5 && key.Length >= 5)
        {
            if (key.StartsWith(folderNorm, StringComparison.Ordinal) || folderNorm.StartsWith(key, StringComparison.Ordinal)) return 1;
            if (key.Length >= 6 && folderNorm.EndsWith(key, StringComparison.Ordinal)) return 1;
            if (folderNorm.Length >= 6 && key.EndsWith(folderNorm, StringComparison.Ordinal)) return 1;
        }
        return 0;
    }
}
