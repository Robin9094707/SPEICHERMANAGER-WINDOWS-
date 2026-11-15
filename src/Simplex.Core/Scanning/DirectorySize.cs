using System.IO.Enumeration;

namespace Simplex.Core.Scanning;

/// <summary>Schnelle, parallele Größenberechnung eines einzelnen Ordners (für Spiele/Programme).</summary>
public static class DirectorySize
{
    private static readonly EnumerationOptions s_opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        BufferSize = 64 * 1024,
    };

    public static long Compute(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            // Dateien direkt im Ordner
            total += SumFiles(path, recurse: false);
            var subdirs = new FileSystemEnumerable<string>(path, (ref FileSystemEntry e) => e.ToFullPath(),
                new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            { ShouldIncludePredicate = (ref FileSystemEntry e) => e.IsDirectory }.ToList();

            Parallel.ForEach(subdirs, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            }, d => Interlocked.Add(ref total, SumFiles(d, recurse: true)));
        }
        catch (OperationCanceledException) { }
        catch { }
        return total;
    }

    private static long SumFiles(string dir, bool recurse)
    {
        long sum = 0;
        try
        {
            var opts = recurse ? s_opts : new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var en = new FileSystemEnumerable<long>(dir, (ref FileSystemEntry e) => e.Length, opts)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,
            };
            foreach (var l in en) sum += l;
        }
        catch { }
        return sum;
    }
}
