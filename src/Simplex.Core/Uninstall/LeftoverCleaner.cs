using System.Diagnostics;
using System.Text;
using Simplex.Core.Safety;

namespace Simplex.Core.Uninstall;

/// <summary>
/// Entfernt ausgewählte Reste. Vorher wird eine Registry-Sicherung (.reg) angelegt und geplante Aufgaben werden als XML
/// gesichert. Jede Datei/jeder Ordner durchläuft erneut den <see cref="SafetyGuard"/>, jeder Registry-Pfad <see cref="RegPath.CanDeleteKey"/>.
/// </summary>
public static class LeftoverCleaner
{
    public static LeftoverCleanResult Clean(IReadOnlyList<LeftoverItem> items, bool toRecycleBin, string backupRoot,
        IProgress<(int Done, int Total, string Text)>? progress = null, CancellationToken ct = default)
    {
        var result = new LeftoverCleanResult();
        int total = items.Count, done = 0;
        void Step(string text) => progress?.Report((++done, total, text));

        // ---- Sicherung ----
        string backupDir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Directory.CreateDirectory(backupDir);
            result.BackupFile = BackupRegistry(items, backupDir);
            foreach (var t in items.Where(i => i.Kind == LeftoverKind.ScheduledTask))
            {
                try
                {
                    string src = Path.Combine(Locations.Windows, "System32", "Tasks") + t.Path;
                    if (File.Exists(src)) File.Copy(src, Path.Combine(backupDir, "Aufgabe_" + Path.GetFileName(t.Path) + ".xml"), true);
                }
                catch { }
            }
            File.WriteAllLines(Path.Combine(backupDir, "Liste.txt"),
                items.Select(i => $"[{i.Kind}] {i.Owner}: {i.Path}{(i.ValueName != null ? " → " + i.ValueName : "")}"), Encoding.UTF8);
        }
        catch { }

        // ---- 1) Dienste & Aufgaben (damit keine Dateien mehr gesperrt sind) ----
        foreach (var s in items.Where(i => i.Kind == LeftoverKind.Service))
        {
            if (ct.IsCancellationRequested) break;
            Step("Dienst " + s.Path);
            if (IsCriticalService(s.Path)) { result.Skipped++; continue; }
            Run("sc.exe", $"stop \"{s.Path}\"", 15000);
            int rc = Run("sc.exe", $"delete \"{s.Path}\"", 15000);
            Record(result, s, rc == 0 || rc == 1072, rc == 5 ? "Zugriff verweigert (Adminrechte nötig)" : $"sc delete: Fehler {rc}");
        }
        foreach (var t in items.Where(i => i.Kind == LeftoverKind.ScheduledTask))
        {
            if (ct.IsCancellationRequested) break;
            Step("Aufgabe " + t.Path);
            if (t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) { result.Skipped++; continue; }
            int rc = Run("schtasks.exe", $"/Delete /TN \"{t.Path}\" /F", 15000);
            Record(result, t, rc == 0, "Aufgabe konnte nicht gelöscht werden (Adminrechte nötig?)");
        }

        // ---- 2) Prozesse aus zu löschenden Ordnern beenden ----
        var folders = items.Where(i => i.Kind == LeftoverKind.Folder).Select(i => Locations.Normalize(i.Path)).ToList();
        if (folders.Count > 0) KillProcessesIn(folders);

        // ---- 3) Dateien, Ordner, Verknüpfungen ----
        foreach (var f in items.Where(i => i.IsFileSystem))
        {
            if (ct.IsCancellationRequested) break;
            Step(f.Path);
            bool isDir = f.Kind == LeftoverKind.Folder;
            if (!(isDir ? Directory.Exists(f.Path) : File.Exists(f.Path))) { Record(result, f, true, ""); continue; }

            if (IsStartMenuOrDesktopEntry(f.Path, isDir))
            {
                bool ok = DeleteShellEntry(f.Path, isDir, toRecycleBin);
                Record(result, f, ok, "Zugriff verweigert (Adminrechte nötig?)");
                if (ok) result.FreedBytes += f.Size;
                continue;
            }

            var outcome = Deleter.Delete(new[] { new DeleteRequest(f.Path, f.Size, SafeAttributes(f.Path), isDir) }, toRecycleBin);
            if (outcome.SkippedProtected > 0) { result.Skipped++; result.Errors.Add(f.Path + ": geschützt – übersprungen"); continue; }
            if (outcome.Deleted > 0)
            {
                result.FreedBytes += outcome.FreedBytes;
                Record(result, f, true, "");
            }
            else if (isDir && !toRecycleBin && DeletePartially(f.Path, out long freed))
            {
                // Gesperrte Einzeldateien bleiben, Rest ist weg
                result.FreedBytes += freed;
                result.Failed++;
                result.Errors.Add(f.Path + ": teilweise gelöscht – einige Dateien sind in Benutzung (nach Neustart erneut versuchen)");
            }
            else
            {
                Record(result, f, false, outcome.Errors.FirstOrDefault() ?? "in Benutzung oder Zugriff verweigert");
            }
        }

        // ---- 4) Registry ----
        foreach (var r in items.Where(i => i.IsRegistry))
        {
            if (ct.IsCancellationRequested) break;
            Step(r.Display);
            try
            {
                if (r.Kind is LeftoverKind.RegistryKey or LeftoverKind.UninstallEntry)
                {
                    if (!RegPath.CanDeleteKey(r.Path)) { result.Skipped++; result.Errors.Add(r.Path + ": geschützter Registry-Bereich"); continue; }
                    var split = RegPath.Split(r.Path);
                    if (split == null) { result.Skipped++; continue; }
                    using var bk = split.Value.Base;
                    string parent = RegPath.Parent(split.Value.SubPath);
                    string leaf = RegPath.Leaf(split.Value.SubPath);
                    using var pk = bk.OpenSubKey(parent, writable: true);
                    if (pk == null) { Record(result, r, true, ""); continue; }
                    pk.DeleteSubKeyTree(leaf, throwOnMissingSubKey: false);
                    Record(result, r, true, "");
                }
                else
                {
                    if (!RegPath.CanDeleteValue(r.Path) || r.ValueName == null) { result.Skipped++; continue; }
                    using var k = RegPath.Open(r.Path, writable: true);
                    k?.DeleteValue(r.ValueName, throwOnMissingValue: false);
                    Record(result, r, true, "");
                }
            }
            catch (UnauthorizedAccessException) { Record(result, r, false, "Zugriff verweigert (Adminrechte nötig)"); }
            catch (System.Security.SecurityException) { Record(result, r, false, "Zugriff verweigert (Adminrechte nötig)"); }
            catch (Exception ex) { Record(result, r, false, ex.Message); }
        }

        return result;
    }

    private static void Record(LeftoverCleanResult res, LeftoverItem item, bool ok, string error)
    {
        if (ok)
        {
            res.Removed++;
            res.RemovedItems.Add(item);
        }
        else
        {
            res.Failed++;
            res.Errors.Add((item.Display.Length > 0 ? item.Display : item.Path) + ": " + error);
        }
    }

    private static FileAttributes SafeAttributes(string path)
    {
        try { return File.GetAttributes(path); } catch { return FileAttributes.Normal; }
    }

    private static readonly HashSet<string> s_criticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "wuauserv", "BITS", "EventLog", "RpcSs", "LanmanServer", "LanmanWorkstation", "Dhcp", "Dnscache", "MpsSvc", "BFE",
        "WSearch", "Schedule", "Winmgmt", "TrustedInstaller", "msiserver", "AudioSrv", "Audiosrv", "AudioEndpointBuilder", "Themes", "CryptSvc",
        "PlugPlay", "Power", "ProfSvc", "SamSs", "Spooler", "UserManager", "W32Time", "WlanSvc", "nsi", "netprofm", "SecurityHealthService",
    };

    private static bool IsCriticalService(string name) => s_criticalServices.Contains(name);

    private static int Run(string exe, string args, int timeoutMs)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return -1;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return -2; }
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static void KillProcessesIn(List<string> folders)
    {
        int self = Environment.ProcessId;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == self) continue;
                string? f = proc.MainModule?.FileName;
                if (f == null || Locations.IsWindowsDir(f)) continue;
                if (folders.Any(d => Locations.IsUnderOrEqual(f, d)))
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }
    }

    /// <summary>Startmenü/Desktop (auch „Alle Benutzer“) sind im SafetyGuard gesperrt – Verknüpfungen und Programmordner darin dürfen aber weg.</summary>
    private static bool IsStartMenuOrDesktopEntry(string path, bool isDir)
    {
        foreach (var root in Locations.StartMenus.Concat(Locations.Desktops))
        {
            if (root.Length == 0 || !Locations.IsUnderOrEqual(path, root) || string.Equals(path.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase)) continue;
            if (!isDir) return path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).All(f =>
                    f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
        return false;
    }

    private static bool DeleteShellEntry(string path, bool isDir, bool toRecycleBin)
    {
        try
        {
            if (toRecycleBin)
            {
                Deleter.RecycleRaw(path);
                return isDir ? !Directory.Exists(path) : !File.Exists(path);
            }
            if (isDir) Directory.Delete(path, true); else File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    private static bool DeletePartially(string dir, out long freed)
    {
        freed = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList())
            {
                try
                {
                    long len = new FileInfo(f).Length;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    freed += len;
                }
                catch { }
            }
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
                try { Directory.Delete(d, false); } catch { }
            try { Directory.Delete(dir, false); } catch { }
            return freed > 0;
        }
        catch { return false; }
    }

    /// <summary>Exportiert alle betroffenen Schlüssel mit reg.exe in eine einzige .reg-Datei (Doppelklick stellt alles wieder her).</summary>
    private static string? BackupRegistry(IReadOnlyList<LeftoverItem> items, string backupDir)
    {
        var keys = new List<string>();
        foreach (var i in items)
        {
            string? k = i.Kind switch
            {
                LeftoverKind.RegistryKey or LeftoverKind.UninstallEntry => i.Path,
                LeftoverKind.RegistryValue or LeftoverKind.FirewallRule => i.Path,
                LeftoverKind.Service => @"HKLM\SYSTEM\CurrentControlSet\Services\" + i.Path,
                _ => null,
            };
            if (k != null && !keys.Contains(k, StringComparer.OrdinalIgnoreCase)) keys.Add(k);
        }
        if (keys.Count == 0) return null;

        string target = Path.Combine(backupDir, "Registry-Sicherung.reg");
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        int n = 0;
        foreach (var k in keys)
        {
            string tmp = Path.Combine(backupDir, $"part{n++}.reg");
            if (Run("reg.exe", $"export \"{k}\" \"{tmp}\" /y", 30000) != 0 || !File.Exists(tmp)) continue;
            try
            {
                var lines = File.ReadAllLines(tmp, Encoding.Unicode);
                foreach (var line in lines.Skip(1)) sb.AppendLine(line);
            }
            catch { }
            try { File.Delete(tmp); } catch { }
        }
        File.WriteAllText(target, sb.ToString(), Encoding.Unicode);
        return target;
    }
}
