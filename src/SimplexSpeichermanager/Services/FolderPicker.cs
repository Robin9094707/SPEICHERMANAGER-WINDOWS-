using System.Runtime.InteropServices;

namespace Simplex.Services;

/// <summary>
/// Klassischer Windows-Ordnerdialog über COM (IFileOpenDialog).
/// Funktioniert im Gegensatz zum WinRT-FolderPicker auch, wenn die App als Administrator läuft.
/// </summary>
public static class FolderPicker
{
    public static string? Pick(IntPtr owner, string? title = null)
    {
        IFileDialog? dialog = null;
        try
        {
            dialog = (IFileDialog)new FileOpenDialogRcw();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            if (title != null) dialog.SetTitle(title);
            int hr = dialog.Show(owner);
            if (hr != 0) return null;
            dialog.GetResult(out IShellItem item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr psz);
            try { return Marshal.PtrToStringUni(psz); }
            finally
            {
                Marshal.FreeCoTaskMem(psz);
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Ordnerauswahl fehlgeschlagen: " + ex.Message);
            return null;
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);                     // IModalWindow
        void SetFileTypes();                                        // Platzhalter (nicht benutzt)
        void SetFileTypeIndex();
        void GetFileTypeIndex();
        void Advise();
        void Unadvise();
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
        void SetDefaultFolder();
        void SetFolder();
        void GetFolder();
        void GetCurrentSelection();
        void SetFileName();
        void GetFileName();
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel();
        void SetFileNameLabel();
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    }
}
