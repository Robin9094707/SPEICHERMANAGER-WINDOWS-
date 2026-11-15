using Simplex.Core;

namespace Simplex.ViewModels;

public static class Glyphs
{
    public static string For(FileCategory c) => c switch
    {
        FileCategory.Video => "",
        FileCategory.Image => "",
        FileCategory.Audio => "",
        FileCategory.Archive => "",
        FileCategory.DiskImage => "",
        FileCategory.VirtualDisk => "",
        FileCategory.Installer => "",
        FileCategory.Document => "",
        FileCategory.Program => "",
        _ => "",
    };
}
