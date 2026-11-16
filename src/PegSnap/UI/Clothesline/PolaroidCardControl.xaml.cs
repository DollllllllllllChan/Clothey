using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PegSnap.Core.Capture;
using PegSnap.Core.Physics;

namespace PegSnap.UI.Clothesline;

public partial class PolaroidCardControl : UserControl
{
    private readonly DampedHarmonicOscillator _oscillator = new(18.0, 0.22, 14.0);
    private readonly Stopwatch _physicsStopwatch = new();
    private readonly DispatcherTimer _physicsTimer;

    private bool _isDragging;
    private double _grabOffsetX;
    private double _grabStartY;
    private double _initialCardLeft;
    private double _initialCardTop;
    private double _lastLeft;

    private BitmapSource? _thumbnail;
    private int _originalWidth;
    private int _originalHeight;

    public BitmapSource OriginalImage => GetFullResolutionImage();
    public string? CachedFilePath { get; private set; }

    public BitmapSource GetFullResolutionImage()
    {
        if (!string.IsNullOrEmpty(CachedFilePath) && File.Exists(CachedFilePath))
        {
            try
            {
                var uri = new Uri(CachedFilePath);
                var dec = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = dec.Frames[0];
                frame.Freeze();
                return frame;
            }
            catch { }
        }

        return _thumbnail ?? (BitmapSource)ThumbnailImage.Source;
    }

    public event EventHandler? RemoveRequested;
    public event EventHandler? PreviewRequested;
    public event EventHandler? CopiedToClipboard;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? DragFinished;

    public PolaroidCardControl()
    {
        InitializeComponent();

        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _physicsTimer.Tick += OnPhysicsTick;

        MouseEnter += OnCardMouseEnter;
        MouseLeave += OnCardMouseLeave;
        MouseDown += OnCardMouseDown;
        MouseMove += OnCardMouseMove;
        MouseUp += OnCardMouseUp;
        CloseButton.Click += OnCloseButtonClick;
    }

    public void SetImageFromCapture(string filePath, BitmapSource thumbnail, int origW, int origH)
    {
        _originalWidth = origW;
        _originalHeight = origH;
        CachedFilePath = filePath;
        _thumbnail = thumbnail;
        ThumbnailImage.Source = thumbnail;

        TimeLabel.Text = DateTime.Now.ToString("HH:mm");
        DimensionsLabel.Text = $" • {origW}×{origH}";

        App.ScheduleWorkingSetTrim(400);
    }

    public void SetImage(BitmapSource bitmap)
    {
        _originalWidth = bitmap.PixelWidth;
        _originalHeight = bitmap.PixelHeight;

        // Generate tiny ~40KB frozen thumbnail for low RAM
        _thumbnail = CreateThumbnail(bitmap, 220);
        ThumbnailImage.Source = _thumbnail;

        TimeLabel.Text = DateTime.Now.ToString("HH:mm");
        DimensionsLabel.Text = $" • {bitmap.PixelWidth}×{bitmap.PixelHeight}";

        // Save original bitmap to disk cache immediately so raw bitmap memory can be collected
        _ = Task.Run(async () =>
        {
            try
            {
                CachedFilePath = await CaptureHelper.SaveToTempFileAsync(bitmap);
                App.ScheduleWorkingSetTrim(400);
            }
            catch { }
        });
    }

    private static BitmapSource CreateThumbnail(BitmapSource source, int targetWidth = 220)
    {
        double scale = (double)targetWidth / Math.Max(1, source.PixelWidth);
        int targetHeight = (int)Math.Max(1, Math.Round(source.PixelHeight * scale));

        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(scaled));

        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;

        var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = dec.Frames[0];
        frame.Freeze();
        return frame;
    }

    public void StartHangingAnimation(double initialImpulseDegrees = 18.0)
    {
        _oscillator.InitialAngleDegrees = initialImpulseDegrees;
        _physicsStopwatch.Restart();
        _physicsTimer.Start();
    }

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        double elapsedSec = _physicsStopwatch.Elapsed.TotalSeconds;

        if (_oscillator.IsSettled(elapsedSec, 0.15))
        {
            _physicsTimer.Stop();
            _physicsStopwatch.Stop();
            CardRotation.Angle = 0;
            return;
        }

        CardRotation.Angle = _oscillator.CalculateAngle(elapsedSec);
    }

    private static readonly Brush BorderNormalBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3C));
    private static readonly Brush BorderActiveBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;

        CloseButton.Opacity = 1.0;
        CardBaseBorder.BorderBrush = BorderActiveBrush;
        CardShadow.Opacity = 0.45;

        var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(1.035, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;

        CloseButton.Opacity = 0.0;
        CardBaseBorder.BorderBrush = BorderNormalBrush;
        CardShadow.Opacity = 0.25;

        var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCopyButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CaptureHelper.CopyToClipboard(OriginalImage, CachedFilePath);
        CopiedToClipboard?.Invoke(this, EventArgs.Empty);
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var canvas = Parent as Canvas;
            if (canvas != null)
            {
                ClotheslineWindow.IsDraggingCard = true;

                // Stop physics animation while actively dragging
                _physicsTimer.Stop();
                _physicsStopwatch.Stop();

                // Clear any running entrance animation clock on TranslateTransform
                CardTranslation.BeginAnimation(TranslateTransform.YProperty, null);
                CardTranslation.BeginAnimation(TranslateTransform.XProperty, null);

                // Detach shadow effect completely during drag for 0 shader overhead and pure 1000Hz GPU blitting
                CardBaseBorder.Effect = null;
                CardRotation.Angle = 0;
                CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                CardScale.ScaleX = 1.0;
                CardScale.ScaleY = 1.0;

                Point mouseOnCanvas = e.GetPosition(canvas);
                double currentLeft = Canvas.GetLeft(this);
                if (double.IsNaN(currentLeft)) currentLeft = 0;
                double currentTop = Canvas.GetTop(this);
                if (double.IsNaN(currentTop)) currentTop = 0;

                _isDragging = true;
                _initialCardLeft = currentLeft;
                _initialCardTop = currentTop;
                _grabOffsetX = mouseOnCanvas.X - currentLeft;
                _grabStartY = mouseOnCanvas.Y;
                _lastLeft = currentLeft;

                CaptureMouse();
            }
        }
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var canvas = Parent as Canvas;
        if (canvas == null) return;

        Point mouseOnCanvas = e.GetPosition(canvas);

        // Exact 1:1 Instant Cursor Tracking using pure GPU TranslateTransform
        // ZERO WPF Layout passes (0 Measure/Arrange overhead)
        double targetLeft = mouseOnCanvas.X - _grabOffsetX;
        CardTranslation.X = targetLeft - _initialCardLeft;

        // Notify ClotheslineWindow so it can update vertical offset along the curved rope
        PositionChanged?.Invoke(this, targetLeft);

        // Keep card rock solid and upright (no seizure-like velocity jitter)
        CardRotation.Angle = 0;

        // Only trigger native drag drop if pulled strongly downwards (> 130px)
        double deltaY = mouseOnCanvas.Y - _grabStartY;
        if (deltaY > 130 && Math.Abs(deltaY) > Math.Abs(mouseOnCanvas.X - (_initialCardLeft + _grabOffsetX)) * 1.5)
        {
            _isDragging = false;
            ClotheslineWindow.IsDraggingCard = false;
            CardBaseBorder.Effect = CardShadow;
            CardShadow.Opacity = 0.25;
            ReleaseMouseCapture();
            CommitTransformPosition();
            StartNativeDragDrop();
        }
    }

    public void UpdateDragVertical(double targetTop)
    {
        CardTranslation.Y = targetTop - _initialCardTop;
    }

    private void CommitTransformPosition()
    {
        double finalX = _initialCardLeft + CardTranslation.X;
        double finalY = _initialCardTop + CardTranslation.Y;
        CardTranslation.X = 0;
        CardTranslation.Y = 0;
        Canvas.SetLeft(this, finalX);
        Canvas.SetTop(this, finalY);
    }

    private void StartNativeDragDrop()
    {
        try
        {
            var dataObj = new DataObject();
            dataObj.SetImage(OriginalImage);

            string? path = CachedFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = CaptureHelper.SaveToTempFileAsync(OriginalImage).GetAwaiter().GetResult();
                CachedFilePath = path;
            }

            if (File.Exists(path))
            {
                var fileCollection = new System.Collections.Specialized.StringCollection { path };
                dataObj.SetFileDropList(fileCollection);
            }

            DragDrop.DoDragDrop(this, dataObj, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DragDrop failed: {ex.Message}");
        }
    }

    private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ClotheslineWindow.IsDraggingCard = false;
        CardBaseBorder.Effect = CardShadow;
        CardShadow.Opacity = 0.25;
        ReleaseMouseCapture();

        double movedDist = Math.Abs(CardTranslation.X);
        double finalX = _initialCardLeft + CardTranslation.X;

        // Commit GPU render translation back to canvas coordinates
        CommitTransformPosition();

        // Settle pendulum rotation smoothly
        StartHangingAnimation(8.0);

        // If card was barely moved (< 5px), treat as single click to open zoom preview
        if (movedDist < 5)
        {
            PreviewRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DragFinished?.Invoke(this, finalX);
        }
    }

    public void AnimateEntrance()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 180,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };

        anim.Completed += (s, e) =>
        {
            CardTranslation.BeginAnimation(TranslateTransform.YProperty, null);
            CardTranslation.Y = 0;
            StartHangingAnimation(18.0);
        };

        CardTranslation.BeginAnimation(TranslateTransform.YProperty, anim);
    }
}
