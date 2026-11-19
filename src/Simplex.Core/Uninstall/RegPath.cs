using Microsoft.Win32;

namespace Simplex.Core.Uninstall;

/// <summary>Hilfen für Registry-Pfade der Form „HKLM\SOFTWARE\…“ bzw. „HKCU\Software\…“ (immer 64-Bit-Ansicht).</summary>
public static class RegPath
{
    public static (RegistryKey Base, string SubPath)? Split(string full)
    {
        int i = full.IndexOf('\\');
        if (i <= 0) return null;
        string hive = full[..i].ToUpperInvariant();
        string sub = full[(i + 1)..];
        RegistryHive? h = hive switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => null,
        };
        if (h == null) return null;
        try { return (RegistryKey.OpenBaseKey(h.Value, RegistryView.Registry64), sub); }
        catch { return null; }
    }

    public static RegistryKey? Open(string full, bool writable = false)
    {
        var s = Split(full);
        if (s == null) return null;
        try { return s.Value.Base.OpenSubKey(s.Value.SubPath, writable); }
        catch { return null; }
        finally { s.Value.Base.Dispose(); }
    }

    public static bool Exists(string full)
    {
        using var k = Open(full);
        return k != null;
    }

    public static string Combine(string a, string b) => a.TrimEnd('\\') + "\\" + b;

    public static string Parent(string full)
    {
        int i = full.TrimEnd('\\').LastIndexOf('\\');
        return i > 0 ? full[..i] : full;
    }

    public static string Leaf(string full)
    {
        int i = full.TrimEnd('\\').LastIndexOf('\\');
        return i >= 0 ? full[(i + 1)..] : full;
    }

    /// <summary>
    /// Nur eng umrissene Bereiche der Registry dürfen gelöscht werden. Alles unter Microsoft, Classes, Policies,
    /// Treibern und Systemschlüsseln ist gesperrt.
    /// </summary>
    public static bool CanDeleteKey(string full)
    {
        string[] p = full.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return false;
        string hive = p[0].ToUpperInvariant();
        if (hive is not ("HKLM" or "HKCU")) return false;
        if (!p[1].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)) return false;

        int i = 2;
        if (p[i].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) i++;
        if (p.Length <= i) return false;

        // …\Microsoft\Windows\CurrentVersion\Uninstall\X bzw. App Paths\X
        if (p[i].Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return p.Length == i + 5 &&
                   p[i + 1].Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
                   p[i + 2].Equals("CurrentVersion", StringComparison.OrdinalIgnoreCase) &&
                   (p[i + 3].Equals("Uninstall", StringComparison.OrdinalIgnoreCase) || p[i + 3].Equals("App Paths", StringComparison.OrdinalIgnoreCase));
        }

        // HKCU\Software\Classes\Applications\foo.exe
        if (p[i].Equals("Classes", StringComparison.OrdinalIgnoreCase))
            return hive == "HKCU" && p.Length == i + 3 && p[i + 1].Equals("Applications", StringComparison.OrdinalIgnoreCase);

        // SOFTWARE\Hersteller bzw. SOFTWARE\Hersteller\Programm
        if (p.Length > i + 2) return false;
        if (s_denyTop.Contains(p[i])) return false;
        return true;
    }

    public static bool CanDeleteValue(string keyPath)
    {
        string k = keyPath.ToUpperInvariant();
        foreach (var allowed in s_valueKeys)
            if (k.EndsWith(allowed, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly string[] s_valueKeys =
    {
        @"\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS\CURRENTVERSION\RUN",
        @"\SOFTWARE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNONCE",
        @"\SOFTWARE\WOW6432NODE\MICROSOFT\WINDOWS\CURRENTVERSION\RUNONCE",
        @"\SYSTEM\CURRENTCONTROLSET\SERVICES\SHAREDACCESS\PARAMETERS\FIREWALLPOLICY\FIREWALLRULES",
    };

    private static readonly HashSet<string> s_denyTop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Classes", "Clients", "Policies", "WOW6432Node", "RegisteredApplications", "ODBC", "Windows", "Intel",
        "NVIDIA Corporation", "NVIDIA", "AMD", "ATI", "ATI Technologies", "Khronos", "Partner", "OEM", "DefaultUserEnvironment",
        "Setup", "System", "Wow6432Node", "Realtek", "Dolby", "Waves", "Synaptics", "Macromedia", "JavaSoft", "Python", "Mozilla",
        "Google", "Apple Inc.", "Apple Computer, Inc.", "Adobe", "Oracle", "Khronos Group", "Cygwin", "Chromium", "AppDataLow",
        "Unity Technologies", "VMware, Inc.", "Docker Inc.", "Sysinternals", "MozillaPlugins", "Wacom", "Logitech", "Razer",
    };
}
