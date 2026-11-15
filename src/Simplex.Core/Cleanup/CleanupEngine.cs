using System.IO.Enumeration;
using Simplex.Core.Native;

namespace Simplex.Core.Cleanup;

public static class CleanupEngine
{
    private static readonly EnumerationOptions s_rec = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, BufferSize = 64 * 1024,
    };
    private static readonly EnumerationOptions s_flat = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, BufferSize = 64 * 1024,
    };

    public static CleanupAnalysis Analyze(CleanupCategory cat, CancellationToken ct = default)
    {
        if (cat.Kind == CleanupKind.RecycleBin)
        {
            try
            {
                var info = new NativeMethods.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
                if (NativeMethods.SHQueryRecycleBinW(null, ref info) == 0)
                    return new CleanupAnalysis { Bytes = info.i64Size, Files = info.i64NumItems };
            }
            catch { }
            return new CleanupAnalysis();
        }

        long bytes = 0, files = 0;
        foreach (var (_, size) in EnumerateFiles(cat, ct))
        {
            bytes += size;
            files++;
        }
        return new CleanupAnalysis { Bytes = bytes, Files = files };
    }

    public static CleanupRunResult Clean(CleanupCategory cat, CancellationToken ct = default)
    {
        var result = new CleanupRunResult();
        if (cat.Kind == CleanupKind.RecycleBin)
        {
            var before = Analyze(cat);
            try
            {
                NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, null,
                    NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
            }
            catch { }
            var after = Analyze(cat);
            result.FreedBytes = Math.Max(0, before.Bytes - after.Bytes);
            result.DeletedFiles = Math.Max(0, before.Files - after.Files);
            return result;
        }

        var files = EnumerateFiles(cat, ct).ToList();
        object gate = new();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount), CancellationToken = ct },
            f =>
            {
                bool ok;
                try
                {
                    var attrs = File.GetAttributes(f.Path);
                    if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(f.Path, attrs & ~FileAttributes.ReadOnly);
                    File.Delete(f.Path);
                    ok = true;
                }
                catch { ok = false; }
                lock (gate)
                {
                    if (ok) { result.FreedBytes += f.Size; result.DeletedFiles++; }
                    else result.FailedFiles++;
                }
            });

        // Leere Unterordner aufräumen (die Zielordner selbst bleiben).
        foreach (var t in cat.Targets.Where(t => t.Recursive && t.Pattern == "*"))
            RemoveEmptySubdirectories(t.Directory);

        return result;
    }

    private static IEnumerable<(string Path, long Size)> EnumerateFiles(CleanupCategory cat, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in cat.Targets)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!seen.Add(t.Directory + "|" + t.Pattern)) continue;
            if (!Directory.Exists(t.Directory)) continue;

            DateTime cutoff = t.MinAgeHours > 0 ? DateTime.UtcNow.AddHours(-t.MinAgeHours) : DateTime.MaxValue;
            string pattern = t.Pattern;
            IEnumerable<(string, long)> en;
            try
            {
                en = new FileSystemEnumerable<(string, long)>(t.Directory,
                    (ref FileSystemEntry e) => (e.ToFullPath(), e.Length),
                    t.Recursive ? s_rec : s_flat)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                        !e.IsDirectory &&
                        (pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, e.FileName)) &&
                        (cutoff == DateTime.MaxValue || e.LastWriteTimeUtc.UtcDateTime < cutoff),
                };
            }
            catch { continue; }

            using var it = en.GetEnumerator();
            while (true)
            {
                (string, long) cur;
                try
                {
                    if (!it.MoveNext()) break;
                    cur = it.Current;
                }
                catch { break; }
                yield return cur;
            }
        }
    }

    private static void RemoveEmptySubdirectories(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var dirs = Directory.EnumerateDirectories(root, "*", s_rec).OrderByDescending(d => d.Length).ToList();
            foreach (var d in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Größe der Ruhezustandsdatei (hiberfil.sys) auf dem Systemlaufwerk.</summary>
    public static long HiberfilSize()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
            var en = new FileSystemEnumerable<long>(root, (ref FileSystemEntry e) => e.Length,
                new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 })
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    !e.IsDirectory && e.FileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase),
            };
            return en.FirstOrDefault();
        }
        catch { return 0; }
    }
}
