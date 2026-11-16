using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PegSnap.Core.Capture;
using PegSnap.Core.Win32;
using PegSnap.UI.Clothesline;
using Xunit;
using Xunit.Abstractions;

namespace PegSnap.Tests;

public class ClotheslineStressTests
{
    private readonly ITestOutputHelper _output;

    public ClotheslineStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void StressTest_20Screenshots_HangingAndDragging_StaysLightweight()
    {
        var logs = new List<string>();

        var thread = new Thread(() =>
        {
            var window = new ClotheslineWindow();
            window.Show();

            NativeMethods.TrimWorkingSet();
            Thread.Sleep(500);

            using var proc = Process.GetCurrentProcess();
            proc.Refresh();
            logs.Add($"| Baseline (Window Empty) | {window.CardsCanvas.Children.Count} cards | {proc.WorkingSet64 / 1048576.0:F2} MB WorkingSet | {proc.PrivateMemorySize64 / 1048576.0:F2} MB Private |");

            // Add 20 real screenshots using zero-copy ProcessCapturedBitmap pipeline
            for (int i = 1; i <= 20; i++)
            {
                using var gdiBmp = new System.Drawing.Bitmap(1920, 1080, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(gdiBmp))
                {
                    g.Clear(System.Drawing.Color.FromArgb(255, (i * 12) % 255, (255 - i * 10) % 255, 180));
                }

                var (filePath, thumbSource, origW, origH) = CaptureHelper.ProcessCapturedBitmap(gdiBmp);
                window.AddCapturedScreenshot(filePath, thumbSource, origW, origH);

                if (i == 1 || i == 5 || i == 10 || i == 15 || i == 20)
                {
                    proc.Refresh();
                    logs.Add($"| Added screenshot #{i,-2} | {window.CardsCanvas.Children.Count,2} cards | {proc.WorkingSet64 / 1048576.0:F2} MB WorkingSet | {proc.PrivateMemorySize64 / 1048576.0:F2} MB Private |");
                }
            }

            // Simulate high-speed 1000Hz drag across the rope
            if (window.CardsCanvas.Children.Count > 0 && window.CardsCanvas.Children[0] is PolaroidCardControl card)
            {
                ClotheslineWindow.IsDraggingCard = true;
                for (double x = 50; x <= 1800; x += 15)
                {
                    double ropeY = window.GetRopeYAtX(x + 78);
                    card.UpdateDragVertical(ropeY - 8);
                }
                ClotheslineWindow.IsDraggingCard = false;
            }

            proc.Refresh();
            logs.Add($"| During/Post-Drag (20)   | {window.CardsCanvas.Children.Count,2} cards | {proc.WorkingSet64 / 1048576.0:F2} MB WorkingSet | {proc.PrivateMemorySize64 / 1048576.0:F2} MB Private |");

            // Allow background disk caching to finalize, then trim working set
            Thread.Sleep(1500);
            NativeMethods.TrimWorkingSet();
            Thread.Sleep(500);

            proc.Refresh();
            double wsAfterTrim = proc.WorkingSet64 / 1048576.0;
            double pmAfterTrim = proc.PrivateMemorySize64 / 1048576.0;
            logs.Add($"| After Idle Trim (20)    | {window.CardsCanvas.Children.Count,2} cards | {wsAfterTrim:F2} MB WorkingSet | {pmAfterTrim:F2} MB Private |");

            // Test ClearAll
            window.ClearAll();
            Thread.Sleep(400);
            NativeMethods.TrimWorkingSet();
            Thread.Sleep(400);

            proc.Refresh();
            logs.Add($"| After ClearAll() + Trim | {window.CardsCanvas.Children.Count,2} cards | {proc.WorkingSet64 / 1048576.0:F2} MB WorkingSet | {proc.PrivateMemorySize64 / 1048576.0:F2} MB Private |");

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        foreach (var log in logs)
        {
            _output.WriteLine(log);
        }
    }

    private static BitmapSource CreateTestBitmap(int width, int height, Color fillColor)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = fillColor.B;
            pixels[i + 1] = fillColor.G;
            pixels[i + 2] = fillColor.R;
            pixels[i + 3] = 255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
