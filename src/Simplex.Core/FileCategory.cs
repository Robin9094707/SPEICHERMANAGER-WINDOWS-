namespace Simplex.Core;

public enum FileCategory
{
    Video,
    Image,
    Audio,
    Archive,
    DiskImage,
    VirtualDisk,
    Installer,
    Document,
    Program,
    Other,
}

public static class FileCategories
{
    public const int Count = (int)FileCategory.Other + 1;

    public static string DisplayName(FileCategory c) => c switch
    {
        FileCategory.Video => "Videos",
        FileCategory.Image => "Bilder",
        FileCategory.Audio => "Musik & Audio",
        FileCategory.Archive => "Archive",
        FileCategory.DiskImage => "Disk-Abbilder (ISO)",
        FileCategory.VirtualDisk => "Virtuelle Festplatten",
        FileCategory.Installer => "Installer",
        FileCategory.Document => "Dokumente",
        FileCategory.Program => "Programm- & Spieldaten",
        _ => "Sonstige",
    };

    /// <summary>Ordnet einen Dateinamen anhand der Endung einer Kategorie zu – ohne Heap-Allokation.</summary>
    public static FileCategory Classify(ReadOnlySpan<char> fileName)
    {
        int dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1) return FileCategory.Other;
        var ext = fileName[(dot + 1)..];
        if (ext.Length > 8) return FileCategory.Other;
        Span<char> buf = stackalloc char[8];
        int n = ext.ToLowerInvariant(buf);
        if (n <= 0) return FileCategory.Other;
        ReadOnlySpan<char> e = buf[..n];
        return e switch
        {
            "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "m4v" or "mpg" or "mpeg" or "ts" or "m2ts" or "mts" or "vob" or "3gp" => FileCategory.Video,
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "tif" or "tiff" or "webp" or "heic" or "heif" or "raw" or "cr2" or "cr3" or "nef" or "arw" or "dng" or "psd" or "svg" => FileCategory.Image,
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "m4a" or "wma" or "opus" or "aiff" or "alac" => FileCategory.Audio,
            "zip" or "rar" or "7z" or "tar" or "gz" or "tgz" or "bz2" or "xz" or "zst" or "cab" or "lz4" => FileCategory.Archive,
            "iso" or "img" or "bin" or "cue" or "nrg" or "dmg" or "wim" or "esd" or "swm" => FileCategory.DiskImage,
            "vhd" or "vhdx" or "vmdk" or "vdi" or "qcow2" or "avhdx" or "hdd" => FileCategory.VirtualDisk,
            "exe" or "msi" or "msix" or "msixbundle" or "appx" or "appxbundle" or "msp" or "apk" => FileCategory.Installer,
            "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "odt" or "csv" or "epub" or "pst" or "ost" or "log" => FileCategory.Document,
            "pak" or "dll" or "bundle" or "assets" or "ucas" or "utoc" or "forge" or "big" or "vpk" or "dat" or "arc" or "rpf" or "upk" or "cache" or "db" or "sqlite" or "blob" => FileCategory.Program,
            _ => FileCategory.Other,
        };
    }
}
