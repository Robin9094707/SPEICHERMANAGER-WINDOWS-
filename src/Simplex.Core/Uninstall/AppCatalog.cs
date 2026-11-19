using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Simplex.Core.Uninstall;

/// <summary>Rohdaten eines Uninstall-Registry-Eintrags (auch versteckte Systemkomponenten).</summary>
public sealed record UninstallRecord(
    string RegistryPath, string KeyName, string Name, string Publisher, string Version, string InstallLocation,
    string UninstallString, string QuietUninstallString, string DisplayIcon, long EstimatedSize, DateTime? InstallDate,
    bool SystemComponent, bool HasParent, bool IsUpdate, bool WindowsInstaller, bool PerUser, bool InnoSetup);

/// <summary>Store-Paket (Hauptpaket oder Framework).</summary>
public sealed record StorePackage(
    string FullName, string FamilyName, string Name, string DisplayName, string Publisher, string Version,
    string InstalledPath, string LogoPath, DateTime? InstallDate, bool IsFramework, bool SystemSigned, bool IsMain);

/// <summary>Liest alle installierten Programme (Registry) und Store-Apps (PackageManager).</summary>
public static class AppCatalog
{
    public const string UninstallSub = @"Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly Regex s_guid = new(@"^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<string> UninstallRoots()
    {
        yield return @"HKLM\SOFTWARE\" + UninstallSub;
        yield return @"HKLM\SOFTWARE\WOW6432Node\" + UninstallSub;
        yield return @"HKCU\Software\" + UninstallSub;
        yield return @"HKCU\Software\WOW6432Node\" + UninstallSub;
    }

    /// <summary>Alle Uninstall-Einträge ohne Filter.</summary>
    public static List<UninstallRecord> ReadUninstallRecords()
    {
        var list = new List<UninstallRecord>();
        foreach (var rootPath in UninstallRoots())
        {
            using var root = RegPath.Open(rootPath);
            if (root == null) continue;
            bool perUser = rootPath.StartsWith("HKCU", StringComparison.Ordinal);
            string[] names;
            try { names = root.GetSubKeyNames(); } catch { continue; }
            foreach (var sub in names)
            {
                try
                {
                    using var k = root.OpenSubKey(sub);
                    if (k == null) continue;
                    string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                    long size = k.GetValue("EstimatedSize") switch
                    {
                        int kb => (long)(uint)kb * 1024,
                        long lkb => lkb * 1024,
                        _ => 0,
                    };
                    DateTime? date = null;
                    if (k.GetValue("InstallDate") is string ds && ds.Length == 8 &&
                        DateTime.TryParseExact(ds, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
                        date = d;
                    string rt = k.GetValue("ReleaseType") as string ?? "";
                    list.Add(new UninstallRecord(
                        RegPath.Combine(rootPath, sub), sub, name,
                        (k.GetValue("Publisher") as string ?? "").Trim(),
                        (k.GetValue("DisplayVersion") as string ?? "").Trim(),
                        CleanPath(k.GetValue("InstallLocation") as string),
                        (k.GetValue("UninstallString") as string ?? "").Trim(),
                        (k.GetValue("QuietUninstallString") as string ?? "").Trim(),
                        (k.GetValue("DisplayIcon") as string ?? "").Trim(),
                        size, date,
                        k.GetValue("SystemComponent") is int sc && sc == 1,
                        k.GetValue("ParentKeyName") != null,
                        rt.Contains("Update", StringComparison.OrdinalIgnoreCase) || rt.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(name, @"\(KB\d{6,}\)"),
                        k.GetValue("WindowsInstaller") is int wi && wi == 1,
                        perUser,
                        k.GetValue("Inno Setup: App Path") != null));
                }
                catch { }
            }
        }
        return list;
    }

    public static string CleanPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"').Trim());
        return p.Length > 3 ? p.TrimEnd('\\', '/') : p;
    }

    /// <summary>Lädt sichtbare Desktop-Programme.</summary>
    public static List<AppEntry> LoadWin32(List<UninstallRecord>? records = null)
    {
        records ??= ReadUninstallRecords();
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Name.Length == 0 || r.SystemComponent || r.HasParent || r.IsUpdate) continue;
            if (r.UninstallString.Length == 0 && r.InstallLocation.Length == 0) continue;
            if (!seen.Add(r.Name + "|" + r.Version)) continue;

            string? msi = r.WindowsInstaller && s_guid.IsMatch(r.KeyName) ? r.KeyName : null;
            if (msi == null)
            {
                var m = Regex.Match(r.UninstallString, @"msiexec(\.exe)?\s+/[IX]\s*(\{[0-9A-F-]{36}\})", RegexOptions.IgnoreCase);
                if (m.Success) msi = m.Groups[2].Value;
            }

            bool missing = msi == null && !UninstallRunner.UninstallerExists(r.UninstallString);
            var (cls, reason) = AppClassifier.ClassifyWin32(r.Name, r.Publisher);
            result.Add(new AppEntry
            {
                Id = r.RegistryPath,
                Name = r.Name,
                Publisher = r.Publisher,
                Version = r.Version,
                Size = r.EstimatedSize,
                InstallDate = r.InstallDate,
                Source = AppSource.Win32,
                Class = cls,
                ClassReason = reason,
                InstallLocation = r.InstallLocation,
                UninstallString = r.UninstallString,
                QuietUninstallString = r.QuietUninstallString,
                RegistryPath = r.RegistryPath,
                MsiProductCode = msi,
                PerUser = r.PerUser,
                DisplayIcon = r.DisplayIcon,
                UninstallerMissing = missing,
            });
        }
        return result;
    }

    /// <summary>Alle Store-Pakete des aktuellen Benutzers (inkl. Frameworks, wenn gewünscht).</summary>
    public static List<StorePackage> ReadStorePackages(bool includeFrameworks)
    {
        var list = new List<StorePackage>();
        try
        {
            var pm = new PackageManager();
            var types = includeFrameworks
                ? PackageTypes.Main | PackageTypes.Framework | PackageTypes.Optional | PackageTypes.Resource | PackageTypes.Bundle
                : PackageTypes.Main;
            foreach (var p in pm.FindPackagesForUserWithPackageTypes("", types))
            {
                try
                {
                    var id = p.Id;
                    string display = id.Name, publisher = "", logo = "", path = "";
                    try { display = p.DisplayName; } catch { }
                    try { publisher = p.PublisherDisplayName; } catch { }
                    try { path = p.InstalledLocation?.Path ?? ""; } catch { }
                    try { logo = p.Logo?.LocalPath ?? ""; } catch { }
                    DateTime? installed = null;
                    try { installed = p.InstalledDate.LocalDateTime; } catch { }
                    bool system = false;
                    try { system = p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System; } catch { }
                    var v = id.Version;
                    list.Add(new StorePackage(
                        id.FullName, id.FamilyName, id.Name,
                        string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? id.Name : display,
                        publisher, $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}", path, logo, installed,
                        p.IsFramework, system, !p.IsFramework && !p.IsResourcePackage && !p.IsBundle && !p.IsOptional));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public static List<AppEntry> LoadStore()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ReadStorePackages(includeFrameworks: false))
        {
            if (!p.IsMain || p.IsFramework) continue;
            if (!seen.Add(p.FamilyName)) continue;
            var (cls, reason) = AppClassifier.ClassifyStore(p.Name, p.SystemSigned);
            result.Add(new AppEntry
            {
                Id = p.FullName,
                Name = p.DisplayName,
                Publisher = p.Publisher,
                Version = p.Version,
                InstallDate = p.InstallDate,
                Source = AppSource.Store,
                Class = cls,
                ClassReason = reason,
                InstallLocation = p.InstalledPath,
                PackageFullName = p.FullName,
                PackageFamilyName = p.FamilyName,
                PackageName = p.Name,
                LogoPath = ResolveLogo(p.LogoPath),
            });
        }
        return result;
    }

    /// <summary>Store-Logos liegen mit Skalierungs-Suffix vor (Logo.scale-200.png) – passende Datei suchen.</summary>
    public static string ResolveLogo(string logo)
    {
        try
        {
            if (string.IsNullOrEmpty(logo)) return "";
            if (File.Exists(logo)) return logo;
            string? dir = Path.GetDirectoryName(logo);
            if (dir == null || !Directory.Exists(dir)) return "";
            string stem = Path.GetFileNameWithoutExtension(logo);
            string ext = Path.GetExtension(logo);
            string? best = null;
            int bestScore = int.MaxValue;
            foreach (var f in Directory.EnumerateFiles(dir, stem + ".*" + ext))
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (n.Contains("contrast-")) continue;
                int score = n.Contains("scale-200") ? 0 : n.Contains("targetsize-48") && !n.Contains("unplated") ? 1 :
                            n.Contains("scale-150") ? 2 : n.Contains("scale-100") ? 3 : 5;
                if (score < bestScore) { bestScore = score; best = f; }
            }
            return best ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Entfernt eine Store-App. Mit Adminrechten optional für alle Benutzer inkl. Bereitstellung (keine Neuinstallation für neue Konten).</summary>
    public static async Task<(bool Ok, string Error)> RemoveStoreAppAsync(AppEntry app, bool allUsers)
    {
        if (app.Class == AppClass.System) return (false, "Systemkomponente – gesperrt");
        try
        {
            var pm = new PackageManager();
            DeploymentResult r;
            if (allUsers && Elevation.IsAdmin)
            {
                try { await pm.DeprovisionPackageForAllUsersAsync(app.PackageFamilyName).AsTask(); } catch { }
                r = await pm.RemovePackageAsync(app.PackageFullName, RemovalOptions.RemoveForAllUsers).AsTask();
            }
            else
            {
                r = await pm.RemovePackageAsync(app.PackageFullName).AsTask();
            }
            bool ok = r.ExtendedErrorCode == null || r.ExtendedErrorCode.HResult == 0;
            return (ok, ok ? "" : r.ErrorText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool StorePackageInstalled(string familyName)
    {
        try { return new PackageManager().FindPackagesForUser("", familyName).Any(); }
        catch { return false; }
    }
}
