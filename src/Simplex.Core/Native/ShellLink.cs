using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Simplex.Core.Native;

/// <summary>Liest das Ziel einer Verknüpfung (.lnk) über die native Shell-Schnittstelle IShellLinkW.</summary>
public static class ShellLink
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    private const uint SLGP_RAWPATH = 0x4;

    /// <summary>Gibt den Zielpfad zurück (Umgebungsvariablen aufgelöst) oder "" bei Store-/MSI-Verknüpfungen ohne Dateipfad.</summary>
    public static string GetTarget(string lnkPath)
    {
        object? obj = null;
        try
        {
            obj = new CShellLink();
            ((IPersistFile)obj).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)obj).GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            return Environment.ExpandEnvironmentVariables(sb.ToString());
        }
        catch { return ""; }
        finally
        {
            if (obj != null) try { Marshal.FinalReleaseComObject(obj); } catch { }
        }
    }
}
