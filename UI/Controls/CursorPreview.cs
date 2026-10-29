using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Colmillo.Core.Windows;

namespace Colmillo.UI.Controls;

/// <summary>
/// Carga un .cur o .ani como imagen. Lo hace el propio Windows (LoadImage): WPF no
/// decodifica cursores, y en un .ani se queda con el primer cuadro.
/// </summary>
public static class CursorPreview
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyCursor(IntPtr cursor);

    const uint IMAGE_CURSOR = 2;
    const uint LR_LOADFROMFILE = 0x10;

    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string path, int size = 32)
    {
        var full = CursorSchemes.Expand(path);
        if (string.IsNullOrWhiteSpace(full)) return null;
        var key = $"{full}|{size}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? image = null;
        if (File.Exists(full))
        {
            var handle = LoadImage(IntPtr.Zero, full, IMAGE_CURSOR, size, size, LR_LOADFROMFILE);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    // HCURSOR y HICON son el mismo tipo de objeto para esta funcion.
                    var src = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    image = src;
                }
                catch (COMException)
                {
                }
                finally
                {
                    DestroyCursor(handle);
                }
            }
        }
        Cache[key] = image;
        return image;
    }
}
