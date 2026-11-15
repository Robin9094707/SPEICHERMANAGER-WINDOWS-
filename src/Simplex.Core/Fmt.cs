using System.Globalization;

namespace Simplex.Core;

/// <summary>Formatierungshelfer für Größen, Zahlen und Zeiten.</summary>
public static class Fmt
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        string format = v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00";
        return v.ToString(format, CultureInfo.CurrentCulture) + " " + Units[i];
    }

    public static string Number(long n) => n.ToString("N0", CultureInfo.CurrentCulture);

    public static string Duration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        if (t.TotalSeconds >= 10) return t.ToString(@"m\:ss", CultureInfo.InvariantCulture) + " min";
        return t.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
    }

    public static string Date(DateTime utc) =>
        utc == default ? "" : utc.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
}
