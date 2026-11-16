using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using PegSnap.Core.Capture;
using Xunit;

namespace PegSnap.Tests;

public class CaptureHelperTests
{
    [Fact]
    public void GetVirtualScreenBounds_ReturnsPositiveDimensions()
    {
        var bounds = CaptureHelper.GetVirtualScreenBounds();
        Assert.True(bounds.Width > 0, "Virtual screen width must be greater than 0");
        Assert.True(bounds.Height > 0, "Virtual screen height must be greater than 0");
    }

    [Fact]
    public void Crop_SubRectangle_ReturnsCorrectSize()
    {
        using var original = new Bitmap(200, 200, PixelFormat.Format32bppArgb);
        using var cropped = CaptureHelper.Crop(original, new Rectangle(10, 10, 50, 60));

        Assert.Equal(50, cropped.Width);
        Assert.Equal(60, cropped.Height);
    }

    [Fact]
    public void ToBitmapSource_ConvertsAndFreezes()
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        var source = CaptureHelper.ToBitmapSource(bmp);

        Assert.NotNull(source);
        Assert.Equal(64, source.PixelWidth);
        Assert.Equal(64, source.PixelHeight);
        Assert.True(source.IsFrozen, "BitmapSource should be frozen for thread safety");
    }

    [Fact]
    public async Task SaveToTempFileAsync_CreatesValidPngFile()
    {
        using var bmp = new Bitmap(40, 40, PixelFormat.Format32bppArgb);
        var source = CaptureHelper.ToBitmapSource(bmp);

        string filePath = await CaptureHelper.SaveToTempFileAsync(source);

        Assert.True(File.Exists(filePath), "Temporary file must exist on disk");
        Assert.True(new FileInfo(filePath).Length > 0, "Saved file must have content");

        // Cleanup
        try { File.Delete(filePath); } catch { }
    }
}
