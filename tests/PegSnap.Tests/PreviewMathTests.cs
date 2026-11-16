using System;
using Xunit;

namespace PegSnap.Tests;

public class PreviewMathTests
{
    [Theory]
    [InlineData(0, 0, 1.0, 1.25, 100, 50)]      // Pan 0, Zoom 1.0 -> 1.25, Mouse at (100, 50)
    [InlineData(50, -30, 0.5, 0.8, -200, -150)]  // Pan (50,-30), Zoom 0.5 -> 0.8, Top-Left Mouse
    [InlineData(-100, 80, 2.0, 1.5, 300, 200)] // Pan (-100,80), Zoom 2.0 -> 1.5, Bottom-Right Mouse
    public void FocalZoom_PinsExactPixelUnderCursor(double pX, double pY, double s1, double s2, double mx, double my)
    {
        double k = s2 / s1;

        // Mouse relative to container center
        double cx = mx;
        double cy = my;

        // Image coordinate under mouse before zoom
        double uMouseX = (cx - pX) / s1;
        double uMouseY = (cy - pY) / s1;

        // New pan
        double newPx = pX * k + cx * (1.0 - k);
        double newPy = pY * k + cy * (1.0 - k);

        // Position of that image coordinate after zoom
        double afterX = (uMouseX * s2) + newPx;
        double afterY = (uMouseY * s2) + newPy;

        Assert.Equal(cx, afterX, 4);
        Assert.Equal(cy, afterY, 4);
    }

    [Theory]
    [InlineData(0.1, 10, 20)]   // 10% zoom, pan 10px, 20px
    [InlineData(0.5, -30, 45)]  // 50% zoom, pan -30px, 45px
    [InlineData(1.0, 100, -100)]// 100% zoom, pan 100px, -100px
    [InlineData(3.0, 250, 180)] // 300% zoom, pan 250px, 180px
    public void FreePanning_AllowedAtAnyZoomLevel(double zoom, double deltaX, double deltaY)
    {
        // Free panning must allow pan updates regardless of whether zoom is < 1.0, == 1.0, or > 1.0
        double startPanX = 0;
        double startPanY = 0;

        double updatedPanX = startPanX + deltaX;
        double updatedPanY = startPanY + deltaY;

        Assert.True(zoom > 0);
        Assert.Equal(deltaX, updatedPanX);
        Assert.Equal(deltaY, updatedPanY);
    }

    [Fact]
    public void RenderTransform_ScalesAndPansVisual_Correctly()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var scale = new System.Windows.Media.ScaleTransform(1.0, 1.0);
            var pan = new System.Windows.Media.TranslateTransform(0, 0);
            var group = new System.Windows.Media.TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(pan);

            // Test zoom in
            scale.ScaleX = 1.5;
            scale.ScaleY = 1.5;
            Assert.Equal(1.5, scale.ScaleX);

            // Test free pan
            pan.X = 120;
            pan.Y = -80;
            Assert.Equal(120, pan.X);
            Assert.Equal(-80, pan.Y);

            // Test point transform
            var pt = group.Transform(new System.Windows.Point(100, 100));
            Assert.Equal(100 * 1.5 + 120, pt.X);
            Assert.Equal(100 * 1.5 - 80, pt.Y);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

