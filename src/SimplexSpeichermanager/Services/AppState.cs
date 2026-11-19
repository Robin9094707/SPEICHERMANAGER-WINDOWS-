using Simplex.Core.Scanning;
using Simplex.Core.Uninstall;

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

    /// <summary>Ergebnis einer Restesuche, das der Reste-Finder als Nächstes anzeigen soll.</summary>
    public static LeftoverSession? PendingLeftovers { get; private set; }
    public static event EventHandler? PendingLeftoversChanged;

    public static void SetPendingLeftovers(LeftoverSession session)
    {
        PendingLeftovers = session;
        PendingLeftoversChanged?.Invoke(null, EventArgs.Empty);
    }

    public static LeftoverSession? TakePendingLeftovers()
    {
        var s = PendingLeftovers;
        PendingLeftovers = null;
        return s;
    }

    public static void AddFreed(long bytes)
    {
        if (bytes <= 0) return;
        Interlocked.Add(ref s_freed, bytes);
        FreedChanged?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>Gefundene Reste nach einer Deinstallation (oder aus der Altlasten-Suche).</summary>
public sealed class LeftoverSession
{
    public required string Title { get; init; }
    public required List<LeftoverItem> Items { get; init; }
    public bool IsOrphanScan { get; init; }
}
