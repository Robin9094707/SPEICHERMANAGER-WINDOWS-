using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Simplex.Core.Uninstall;

public enum UninstallStatus
{
    /// <summary>Deinstaller ist durchgelaufen, Eintrag ist verschwunden.</summary>
    Completed,
    /// <summary>Deinstaller beendet, aber der Eintrag existiert noch (abgebrochen oder Neustart nötig).</summary>
    StillInstalled,
    /// <summary>UAC-Abfrage abgelehnt.</summary>
    Cancelled,
    /// <summary>Deinstaller fehlt – nur Reste entfernen.</summary>
    UninstallerMissing,
    Failed,
}

public sealed record UninstallCommand(string File, string Arguments, bool Silent);

/// <summary>Startet Deinstaller (auch still) und wartet zuverlässig auf deren Ende – inkl. Kindprozesse von NSIS/Inno.</summary>
public static class UninstallRunner
{
    private static readonly Regex s_innoExe = new(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Zerlegt eine Befehlszeile in Datei und Argumente (auch ohne Anführungszeichen mit Leerzeichen im Pfad).</summary>
    public static (string File, string Args) Split(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.Length == 0) return ("", "");
        if (command[0] == '"')
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
            return (command.Trim('"'), "");
        }
        // Längsten existierenden Pfad suchen (C:\Program Files\Foo\uninst.exe /x)
        int idx = 0;
        while (true)
        {
            int exe = command.IndexOf(".exe", idx, StringComparison.OrdinalIgnoreCase);
            if (exe < 0) break;
            string file = command[..(exe + 4)];
            if (File.Exists(file) || !file.Contains('\\')) return (file, command[(exe + 4)..].Trim());
            idx = exe + 4;
        }
        int sp = command.IndexOf(' ');
        return sp > 0 ? (command[..sp], command[(sp + 1)..].Trim()) : (command, "");
    }

    public static string? ResolveFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (file.Contains('\\') || file.Contains('/')) return File.Exists(file) ? file : null;
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var dir in new[] { sys, win })
        {
            string p = Path.Combine(dir, file);
            if (File.Exists(p)) return p;
            if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p + ".exe")) return p + ".exe";
        }
        return null;
    }

    public static bool UninstallerExists(string uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString)) return false;
        var (file, args) = Split(uninstallString);
        string? resolved = ResolveFile(file);
        if (resolved == null) return false;
        // rundll32/cmd verweisen oft auf eine weitere Datei
        string name = Path.GetFileName(resolved).ToLowerInvariant();
        if (name is "rundll32.exe" or "cmd.exe" or "wscript.exe")
        {
            var m = Regex.Match(args, @"([A-Za-z]:\\[^,""]+?\.(dll|exe|vbs|cmd|bat))", RegexOptions.IgnoreCase);
            if (m.Success) return File.Exists(m.Groups[1].Value);
        }
        return true;
    }

    public static UninstallCommand? Build(AppEntry app, bool silent)
    {
        if (app.MsiProductCode is { } code)
        {
            string msi = ResolveFile("msiexec.exe") ?? "msiexec.exe";
            return new UninstallCommand(msi, "/x " + code + (silent ? " /qb- /norestart REBOOT=ReallySuppress" : " /norestart"), silent);
        }

        if (silent && app.QuietUninstallString.Length > 0 && UninstallerExists(app.QuietUninstallString))
        {
            var (qf, qa) = Split(app.QuietUninstallString);
            return new UninstallCommand(ResolveFile(qf) ?? qf, qa, true);
        }

        if (!UninstallerExists(app.UninstallString)) return null;
        var (file, args) = Split(app.UninstallString);
        string resolved = ResolveFile(file) ?? file;

        // msiexec /I{GUID} → /X{GUID}
        if (Path.GetFileName(resolved).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            args = Regex.Replace(args, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase);
            if (silent && !args.Contains("/q", StringComparison.OrdinalIgnoreCase)) args += " /qb- /norestart";
            return new UninstallCommand(resolved, args, silent);
        }

        if (silent)
        {
            string name = Path.GetFileName(resolved);
            if (s_innoExe.IsMatch(name))
                return new UninstallCommand(resolved, (args + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART").Trim(), true);
            if (IsNsis(resolved) && !args.Contains("/S", StringComparison.Ordinal))
                return new UninstallCommand(resolved, (args + " /S").Trim(), true);
            if (name.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) && args.Contains("--uninstall", StringComparison.OrdinalIgnoreCase))
                return new UninstallCommand(resolved, args.Contains(" -s", StringComparison.Ordinal) ? args : args + " -s", true);
        }
        return new UninstallCommand(resolved, args, false);
    }

    /// <summary>Erkennt NSIS-Deinstaller an ihrer Signatur „NullsoftInst“.</summary>
    private static bool IsNsis(string exe)
    {
        try
        {
            using var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int len = (int)Math.Min(fs.Length, 4 * 1024 * 1024);
            var buf = new byte[len];
            int read = fs.Read(buf, 0, len);
            var sig = Encoding.ASCII.GetBytes("NullsoftInst");
            return buf.AsSpan(0, read).IndexOf(sig) >= 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Führt den Deinstaller aus und wartet, bis er (samt Hilfsprozessen) fertig ist oder der Eintrag verschwindet.
    /// Über <paramref name="ct"/> kann das Warten jederzeit beendet werden (Deinstaller läuft dann weiter).
    /// </summary>
    public static async Task<UninstallStatus> RunAsync(AppEntry app, bool silent, IProgress<string>? status, CancellationToken ct)
    {
        var cmd = Build(app, silent);
        if (cmd == null) return UninstallStatus.UninstallerMissing;

        Process? p;
        try
        {
            var psi = new ProcessStartInfo(cmd.File, cmd.Arguments)
            {
                UseShellExecute = true, // nötig, damit Windows die UAC-Abfrage des Deinstallers zeigt
                WorkingDirectory = Path.GetDirectoryName(cmd.File) is { Length: > 0 } wd && Directory.Exists(wd) ? wd : Environment.CurrentDirectory,
            };
            p = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return UninstallStatus.Cancelled;
        }
        catch
        {
            return UninstallStatus.Failed;
        }

        status?.Report(cmd.Silent ? "Wird still deinstalliert…" : "Bitte den Anweisungen des Deinstallers folgen…");
        try
        {
            if (p != null) await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException) { return Exists(app) ? UninstallStatus.StillInstalled : UninstallStatus.Completed; }

        // NSIS (Au_.exe), Inno (_iu*.tmp), Squirrel u. a. starten eine Kopie und beenden sich sofort.
        int quiet = 0;
        var sw = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested && sw.Elapsed < TimeSpan.FromMinutes(45))
        {
            if (!Exists(app)) return UninstallStatus.Completed;
            if (HelperRunning(app)) quiet = 0;
            else if (++quiet >= 6) break; // 3 s ohne Hilfsprozess
            try { await Task.Delay(500, ct); } catch { break; }
        }
        return Exists(app) ? UninstallStatus.StillInstalled : UninstallStatus.Completed;
    }

    private static bool Exists(AppEntry app) => app.RegistryPath.Length > 0 && RegPath.Exists(app.RegistryPath);

    private static bool HelperRunning(AppEntry app)
    {
        string loc = app.InstallLocation;
        string temp = Path.GetTempPath();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                string n = proc.ProcessName;
                if (n.StartsWith("Au_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Un_", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("_iu", StringComparison.OrdinalIgnoreCase) || n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("uninst", StringComparison.OrdinalIgnoreCase) || n.Contains("deinst", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (n.Equals("msiexec", StringComparison.OrdinalIgnoreCase) || n.Equals("svchost", StringComparison.OrdinalIgnoreCase)) continue;
                string? path = null;
                try { path = proc.MainModule?.FileName; } catch { }
                if (path == null) continue;
                if (path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
                if (loc.Length > 3 && path.StartsWith(loc + "\\", StringComparison.OrdinalIgnoreCase) &&
                    (n.Contains("setup", StringComparison.OrdinalIgnoreCase) || n.Contains("install", StringComparison.OrdinalIgnoreCase) ||
                     n.Equals("Update", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch { }
            finally { proc.Dispose(); }
        }
        return false;
    }

    /// <summary>Erstellt (falls Adminrechte vorhanden) einen Systemwiederherstellungspunkt.</summary>
    public static async Task<bool> CreateRestorePointAsync(string description)
    {
        if (!Elevation.IsAdmin) return false;
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '" + description.Replace("'", "") +
                "' -RestorePointType APPLICATION_UNINSTALL -ErrorAction Stop\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
