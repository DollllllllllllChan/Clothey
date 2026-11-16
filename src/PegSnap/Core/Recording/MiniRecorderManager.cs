using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PegSnap.Core.Capture;

namespace PegSnap.Core.Recording;

public class MiniRecorderManager
{
    private readonly DispatcherTimer _recordTimer;
    private readonly List<BitmapSource> _capturedFrames = new();
    private DateTime _recordStartTime;
    private Rectangle _captureArea;

    public bool IsRecording { get; private set; }
    public TimeSpan ElapsedDuration => IsRecording ? DateTime.Now - _recordStartTime : TimeSpan.Zero;

    public event EventHandler<TimeSpan>? Tick;
    public event EventHandler<BitmapSource>? RecordingCompleted;

    public MiniRecorderManager()
    {
        // 10 FPS capture timer for ultra low CPU consumption
        _recordTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _recordTimer.Tick += OnRecordTimerTick;
    }

    public void Start(Rectangle? area = null)
    {
        if (IsRecording) return;

        _captureArea = area ?? CaptureHelper.GetVirtualScreenBounds();
        _capturedFrames.Clear();
        _recordStartTime = DateTime.Now;
        IsRecording = true;

        _recordTimer.Start();
    }

    private void OnRecordTimerTick(object? sender, EventArgs e)
    {
        if (!IsRecording) return;

        Tick?.Invoke(this, ElapsedDuration);

        // Capture thumbnail frame
        try
        {
            using var fullScreen = CaptureHelper.CaptureVirtualScreen();
            using var cropped = CaptureHelper.Crop(fullScreen, _captureArea);
            var frame = CaptureHelper.ToBitmapSource(cropped);
            if (_capturedFrames.Count < 50) // limit max frames for memory safety (<50MB)
            {
                _capturedFrames.Add(frame);
            }
        }
        catch { }

        // Auto-stop after 15 seconds max
        if (ElapsedDuration.TotalSeconds >= 15)
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsRecording) return;

        IsRecording = false;
        _recordTimer.Stop();

        // Return first or representative frame with "REC" badge overlay
        BitmapSource? resultFrame = _capturedFrames.Count > 0 ? _capturedFrames[0] : null;
        if (resultFrame != null)
        {
            RecordingCompleted?.Invoke(this, resultFrame);
        }
    }
}
