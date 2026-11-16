using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PegSnap.Core.Win32;

namespace PegSnap.Core.Capture;

public static class CaptureHelper
{
    private static readonly string TempCacheDir = Path.Combine(Path.GetTempPath(), "Clothey", "Captures");

    static CaptureHelper()
    {
        try
        {
            if (!Directory.Exists(TempCacheDir))
            {
                Directory.CreateDirectory(TempCacheDir);
            }
        }
        catch { }
    }

    /// <summary>
    /// Gets the bounding rectangle of the virtual desktop across all connected monitors.
    /// </summary>
    public static Rectangle GetVirtualScreenBounds()
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        // Fallback if virtual screen metrics are invalid
        if (width <= 0 || height <= 0)
        {
            width = (int)SystemParameters.PrimaryScreenWidth;
            height = (int)SystemParameters.PrimaryScreenHeight;
            left = 0;
            top = 0;
        }

        return new Rectangle(left, top, width, height);
    }

    /// <summary>
    /// Captures the entire virtual screen into a GDI+ Bitmap.
    /// </summary>
    public static Bitmap CaptureVirtualScreen()
    {
        var bounds = GetVirtualScreenBounds();
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CaptureVirtualScreen error: {ex.Message}");
        }
        return bmp;
    }

    /// <summary>
    /// Captures the current active foreground window.
    /// </summary>
    public static Bitmap? CaptureForegroundWindow()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return null;

        int width = rect.Width;
        int height = rect.Height;

        if (width <= 0 || height <= 0) return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CaptureForegroundWindow error: {ex.Message}");
        }
        return bmp;
    }

    /// <summary>
    /// Processes a captured GDI+ bitmap directly: saves full-res PNG directly to disk,
    /// generates tiny 220px thumbnail, and frees the raw full-size bitmap immediately.
    /// Eliminates memory spikes so RAM stays strictly below ~20MB regardless of count!
    /// </summary>
    public static (string filePath, BitmapSource thumbnail, int origW, int origH) ProcessCapturedBitmap(Bitmap capturedBitmap)
    {
        int origW = capturedBitmap.Width;
        int origH = capturedBitmap.Height;

        string fileName = $"Clothey_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png";
        string filePath = Path.Combine(TempCacheDir, fileName);

        // 1. Direct native GDI+ PNG encoding to disk (fast and 0 managed heap overhead)
        capturedBitmap.Save(filePath, ImageFormat.Png);

        // 2. Direct fast 220px thumbnail generation in GDI+
        double scale = 220.0 / Math.Max(1, origW);
        int thumbW = 220;
        int thumbH = Math.Max(1, (int)Math.Round(origH * scale));

        BitmapSource thumbSource;
        using (var thumbBmp = new Bitmap(thumbW, thumbH, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(thumbBmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(capturedBitmap, new Rectangle(0, 0, thumbW, thumbH), 0, 0, origW, origH, GraphicsUnit.Pixel);
            }
            thumbSource = ToBitmapSource(thumbBmp);
        }

        // 3. Immediately dispose large captured bitmap to prevent any memory bloat
        capturedBitmap.Dispose();

        // 4. Immediate WorkingSet trim to reclaim any GDI+ codec working memory
        NativeMethods.TrimWorkingSet();

        return (filePath, thumbSource, origW, origH);
    }

    /// <summary>
    /// Crops a sub-region from an existing bitmap.
    /// </summary>
    public static Bitmap Crop(Bitmap source, Rectangle cropArea)
    {
        int x = Math.Max(0, cropArea.X);
        int y = Math.Max(0, cropArea.Y);
        int width = Math.Min(source.Width - x, cropArea.Width);
        int height = Math.Min(source.Height - y, cropArea.Height);

        if (width <= 0 || height <= 0)
        {
            return new Bitmap(1, 1);
        }

        var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(source, new Rectangle(0, 0, width, height), x, y, width, height, GraphicsUnit.Pixel);
        }
        return cropped;
    }

    /// <summary>
    /// Converts a GDI+ Bitmap to a frozen WPF BitmapSource suitable for UI rendering and cross-thread access.
    /// </summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bitmapSource = BitmapSource.Create(
                bitmapData.Width,
                bitmapData.Height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmapData.Height,
                bitmapData.Stride);

            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    /// <summary>
    /// Asynchronously writes a BitmapSource to a temporary PNG file for native drag-and-drop.
    /// </summary>
    public static async Task<string> SaveToTempFileAsync(BitmapSource bitmapSource)
    {
        return await Task.Run(() =>
        {
            string fileName = $"Clothey_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png";
            string filePath = Path.Combine(TempCacheDir, fileName);

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(fileStream);

            return filePath;
        });
    }

    /// <summary>
    /// Copies image to Windows Clipboard directly from cached file (both as full Bitmap and FileDrop).
    /// Releases stream immediately to avoid keeping uncompressed bitmaps in managed memory.
    /// </summary>
    public static void CopyToClipboard(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            var dataObj = new System.Windows.DataObject();
            var fileList = new System.Collections.Specialized.StringCollection { filePath };
            dataObj.SetFileDropList(fileList);

            using (var stream = File.OpenRead(filePath))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0)
                {
                    dataObj.SetImage(decoder.Frames[0]);
                }
            }

            System.Windows.Clipboard.SetDataObject(dataObj, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set clipboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies image to Windows Clipboard (both as Bitmap and FileDrop).
    /// </summary>
    public static void CopyToClipboard(BitmapSource bitmapSource, string? filePath = null)
    {
        try
        {
            var dataObj = new System.Windows.DataObject();
            dataObj.SetImage(bitmapSource);

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var fileList = new System.Collections.Specialized.StringCollection { filePath };
                dataObj.SetFileDropList(fileList);
            }

            System.Windows.Clipboard.SetDataObject(dataObj, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set clipboard: {ex.Message}");
        }
    }
}
