using Simplex.Core.Scanning;

namespace Simplex.Services;

/// <summary>Gemeinsamer Zustand aller Seiten.</summary>
public static class AppState
{
    private static long s_freed;

    public static ScanResult? LastScan { get; private set; }
    public static int ScanVersion { get; private set; }

    public static long FreedThisSession => Interlocked.Read(ref s_freed);

    public static event EventHandler? FreedChanged;
    public static event EventHandler? ScanChanged;

    public static void SetScan(ScanResult result)
    {
        LastScan = result;
        ScanVersion++;
        ScanChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void AddFreed(long bytes)
    {
        if (bytes <= 0) return;
        Interlocked.Add(ref s_freed, bytes);
        FreedChanged?.Invoke(null, EventArgs.Empty);
    }
}
