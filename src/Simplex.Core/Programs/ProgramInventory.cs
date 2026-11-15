using System.Diagnostics;
using Microsoft.Win32;

namespace Simplex.Core.Programs;

public sealed class InstalledProgram
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long Size { get; set; }
    public bool SizeFromRegistry { get; init; }
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public string RegistryKey { get; init; } = "";
    public bool PerUser { get; init; }
}

public static class ProgramInventory
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledProgram> Load()
    {
        var result = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, false, result, seen);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, false, result, seen);
        Read(RegistryHive.CurrentUser, RegistryView.Registry64, true, result, seen);
        Read(RegistryHive.CurrentUser, RegistryView.Registry32, true, result, seen);
        result.Sort((a, b) => b.Size.CompareTo(a.Size));
        return result;
    }

    private static void Read(RegistryHive hive, RegistryView view, bool perUser, List<InstalledProgram> list, HashSet<string> seen)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root == null) return;
            foreach (var sub in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(sub);
                    if (k == null) continue;
                    if (k.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") != null) continue;
                    if (k.GetValue("ReleaseType") is string rt && (rt.Contains("Update") || rt.Contains("Hotfix"))) continue;
                    string uninstall = k.GetValue("UninstallString") as string ?? "";
                    if (string.IsNullOrWhiteSpace(uninstall)) continue;

                    string version = k.GetValue("DisplayVersion") as string ?? "";
                    if (!seen.Add(name + "|" + version)) continue;

                    long size = 0;
                    var est = k.GetValue("EstimatedSize");
                    if (est is int kb) size = (long)(uint)kb * 1024;
                    else if (est is long lkb) size = lkb * 1024;

                    DateTime? date = null;
                    if (k.GetValue("InstallDate") is string ds && ds.Length == 8 &&
                        DateTime.TryParseExact(ds, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
                        date = d;

                    list.Add(new InstalledProgram
                    {
                        Name = name.Trim(),
                        Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                        Version = version,
                        Size = size,
                        SizeFromRegistry = size > 0,
                        InstallLocation = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'),
                        UninstallString = uninstall,
                        QuietUninstallString = k.GetValue("QuietUninstallString") as string ?? "",
                        InstallDate = date,
                        RegistryKey = sub,
                        PerUser = perUser,
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Startet den Deinstaller des Programms (dieser fragt selbst nach Bestätigung/UAC).</summary>
    public static bool Uninstall(InstalledProgram p)
    {
        try
        {
            string cmd = p.UninstallString.Trim();
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
