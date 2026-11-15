using Simplex.Core.Native;

namespace Simplex.Core.Safety;

public sealed class DeleteOutcome
{
    public int Deleted { get; set; }
    public int Failed { get; set; }
    public int SkippedProtected { get; set; }
    public long FreedBytes { get; set; }
    public List<string> DeletedPaths { get; } = new();
    public List<string> Errors { get; } = new();
}

public readonly record struct DeleteRequest(string Path, long Size, FileAttributes Attributes, bool IsDirectory = false);

/// <summary>Löscht Dateien/Ordner – immer erst nach erneuter Sicherheitsprüfung.</summary>
public static class Deleter
{
    public static DeleteOutcome Delete(IEnumerable<DeleteRequest> requests, bool toRecycleBin, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var outcome = new DeleteOutcome();
        int done = 0;
        foreach (var r in requests)
        {
            if (ct.IsCancellationRequested) break;
            var verdict = r.IsDirectory ? SafetyGuard.CheckDirectory(r.Path) : SafetyGuard.CheckFile(r.Path, r.Attributes);
            if (verdict.Level == SafetyLevel.Protected)
            {
                outcome.SkippedProtected++;
                continue;
            }

            try
            {
                bool ok = toRecycleBin ? Recycle(r.Path) : DeletePermanently(r.Path, r.IsDirectory);
                bool gone = r.IsDirectory ? !Directory.Exists(r.Path) : !File.Exists(r.Path);
                if (ok && gone)
                {
                    outcome.Deleted++;
                    outcome.FreedBytes += r.Size;
                    outcome.DeletedPaths.Add(r.Path);
                }
                else
                {
                    outcome.Failed++;
                    outcome.Errors.Add($"{Path.GetFileName(r.Path)}: Datei ist in Benutzung oder Zugriff verweigert");
                }
            }
            catch (Exception ex)
            {
                outcome.Failed++;
                outcome.Errors.Add($"{Path.GetFileName(r.Path)}: {ex.Message}");
            }
            progress?.Report(++done);
        }
        return outcome;
    }

    private static bool Recycle(string path)
    {
        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT |
                     NativeMethods.FOF_NOERRORUI | NativeMethods.FOF_NOCONFIRMMKDIR,
        };
        int rc = NativeMethods.SHFileOperationW(ref op);
        return rc == 0 && op.fAnyOperationsAborted == 0;
    }

    private static bool DeletePermanently(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            ClearReadOnly(path);
            Directory.Delete(path, recursive: true);
            return true;
        }
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        File.Delete(path);
        return true;
    }

    private static void ClearReadOnly(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFileSystemEntries(dir, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                try
                {
                    var a = File.GetAttributes(f);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }
        catch { }
    }
}
