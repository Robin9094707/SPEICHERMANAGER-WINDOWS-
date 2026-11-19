using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Simplex.Services;

/// <summary>
/// Lädt echte Programmsymbole nativ (SHDefExtractIcon → GDI → WriteableBitmap) bzw. Store-Logos.
/// Pixel werden im Hintergrund gelesen, nur das Bitmap entsteht auf dem UI-Thread.
/// </summary>
public static class IconLoader
{
    private const int Size = 64;
    private static readonly ConcurrentDictionary<string, byte[]?> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Liest BGRA-Pixel (vormultipliziert) eines Symbols. Im Hintergrund aufrufen.</summary>
    public static byte[]? ReadIconPixels(string file, int index)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
        return s_cache.GetOrAdd(file + "," + index, _ => Extract(file, index));
    }

    public static ImageSource? CreateBitmap(byte[]? pixels)
    {
        if (pixels == null) return null;
        try
        {
            var wb = new WriteableBitmap(Size, Size);
            using (var s = wb.PixelBuffer.AsStream()) s.Write(pixels, 0, pixels.Length);
            wb.Invalidate();
            return wb;
        }
        catch { return null; }
    }

    public static async Task<ImageSource?> LoadImageFileAsync(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            byte[] bytes = await Task.Run(() => File.ReadAllBytes(path));
            var bmp = new BitmapImage { DecodePixelWidth = 64 };
            using var ms = new MemoryStream(bytes);
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            return bmp;
        }
        catch { return null; }
    }

    private static byte[]? Extract(string file, int index)
    {
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            int hr = SHDefExtractIconW(file, index, 0, out large, out small, Size | (16 << 16));
            if (hr != 0 || large == IntPtr.Zero)
            {
                if (ExtractIconExW(file, index, out large, out small, 1) == 0 || large == IntPtr.Zero) return null;
            }
            return ToPixels(large);
        }
        catch { return null; }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    private static byte[]? ToPixels(IntPtr hIcon)
    {
        IntPtr hdcScreen = IntPtr.Zero, hdc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hdcScreen = GetDC(IntPtr.Zero);
            hdc = CreateCompatibleDC(hdcScreen);
            var bmi = new BITMAPINFO
            {
                biSize = Marshal.SizeOf<BITMAPINFO>(),
                biWidth = Size,
                biHeight = -Size, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = CreateDIBSection(hdc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return null;
            old = SelectObject(hdc, dib);
            if (!DrawIconEx(hdc, 0, 0, hIcon, Size, Size, 0, IntPtr.Zero, 3 /* DI_NORMAL */)) return null;
            GdiFlush();

            var px = new byte[Size * Size * 4];
            Marshal.Copy(bits, px, 0, px.Length);

            // Alte Symbole ohne Alphakanal: Maske zeichnen und auswerten
            bool anyAlpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha)
            {
                var mask = new byte[px.Length];
                // Weißer Hintergrund + DI_MASK: transparente Pixel bleiben weiß
                for (int i = 0; i < mask.Length; i++) mask[i] = 0xFF;
                Marshal.Copy(mask, 0, bits, mask.Length);
                DrawIconEx(hdc, 0, 0, hIcon, Size, Size, 0, IntPtr.Zero, 1 /* DI_MASK */);
                GdiFlush();
                Marshal.Copy(bits, mask, 0, mask.Length);
                for (int i = 0; i < px.Length; i += 4)
                    px[i + 3] = mask[i] > 0x80 ? (byte)0 : (byte)0xFF;
            }

            // DrawIconEx liefert vormultiplizierte Werte; vollständig transparente Pixel bereinigen
            for (int i = 0; i < px.Length; i += 4)
                if (px[i + 3] == 0) px[i] = px[i + 1] = px[i + 2] = 0;
            return px;
        }
        catch { return null; }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(hdc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (hdc != IntPtr.Zero) DeleteDC(hdc);
            if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        public int bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, int nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
