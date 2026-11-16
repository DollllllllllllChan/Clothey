using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PegSnap.Core.Capture;
using PegSnap.Core.Win32;
using Point = System.Windows.Point;

namespace PegSnap.UI.Snipping;

public enum SnipMode
{
    StandardArea,
    OcrArea
}

public partial class SnippingWindow : Window
{
    private Bitmap? _cachedFullScreen;
    private Point _startPoint;
    private bool _isDragging;
    private SnipMode _mode = SnipMode.StandardArea;

    public event EventHandler<(string filePath, BitmapSource thumbnail, int origW, int origH)>? AreaCaptured;
    public event EventHandler<(string filePath, BitmapSource thumbnail, int origW, int origH)>? OcrCaptured;

    public SnippingWindow()
    {
        InitializeComponent();

        MouseDown += OnWindowMouseDown;
        MouseMove += OnWindowMouseMove;
        MouseUp += OnWindowMouseUp;
        KeyDown += OnWindowKeyDown;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Center the top banner horizontally
        double centerX = (Width - BannerBorder.ActualWidth) / 2.0;
        Canvas.SetLeft(BannerBorder, Math.Max(20, centerX));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);
        }
    }

    public void StartSnip(SnipMode mode = SnipMode.StandardArea)
    {
        _mode = mode;
        _isDragging = false;
        SelectionBox.Visibility = Visibility.Collapsed;
        DimensionPill.Visibility = Visibility.Collapsed;

        if (_mode == SnipMode.OcrArea)
        {
            BannerText.Text = "Drag cursor around text to extract via OCR  •  Press ESC to cancel";
        }
        else
        {
            BannerText.Text = "Drag cursor to snip selection  •  Press ESC to cancel";
        }

        // Capture full screen into memory before showing overlay
        _cachedFullScreen?.Dispose();
        _cachedFullScreen = CaptureHelper.CaptureVirtualScreen();

        // Cover entire virtual screen in WPF DIPs
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Show();
        Activate();

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _startPoint = e.GetPosition(OverlayCanvas);
            _isDragging = true;

            Canvas.SetLeft(SelectionBox, _startPoint.X);
            Canvas.SetTop(SelectionBox, _startPoint.Y);
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;
            SelectionBox.Visibility = Visibility.Visible;

            DimensionPill.Visibility = Visibility.Visible;
            CaptureMouse();
        }
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(OverlayCanvas);

        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double width = Math.Abs(_startPoint.X - currentPoint.X);
        double height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(SelectionBox, x);
        Canvas.SetTop(SelectionBox, y);
        SelectionBox.Width = width;
        SelectionBox.Height = height;

        // Position dimension pill near cursor
        Canvas.SetLeft(DimensionPill, Math.Min(Width - 120, x + width + 8));
        Canvas.SetTop(DimensionPill, Math.Min(Height - 40, y + height + 8));

        double scaleX = _cachedFullScreen != null && Width > 0 ? (double)_cachedFullScreen.Width / Width : 1.0;
        double scaleY = _cachedFullScreen != null && Height > 0 ? (double)_cachedFullScreen.Height / Height : 1.0;
        DimensionText.Text = $"{(int)Math.Round(width * scaleX)} × {(int)Math.Round(height * scaleY)} px";
    }

    private async void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();

        double x = Canvas.GetLeft(SelectionBox);
        double y = Canvas.GetTop(SelectionBox);
        double width = SelectionBox.Width;
        double height = SelectionBox.Height;

        Hide();
        NativeMethods.TrimWorkingSet();

        if (width >= 6 && height >= 6 && _cachedFullScreen != null)
        {
            double scaleX = Width > 0 ? (double)_cachedFullScreen.Width / Width : 1.0;
            double scaleY = Height > 0 ? (double)_cachedFullScreen.Height / Height : 1.0;

            int cropX = (int)Math.Round(x * scaleX);
            int cropY = (int)Math.Round(y * scaleY);
            int cropW = (int)Math.Round(width * scaleX);
            int cropH = (int)Math.Round(height * scaleY);

            var cropRect = new Rectangle(cropX, cropY, cropW, cropH);
            var mode = _mode;
            var fullScreenBmp = _cachedFullScreen;
            _cachedFullScreen = null;

            await Task.Run(() =>
            {
                using var cropped = CaptureHelper.Crop(fullScreenBmp, cropRect);
                fullScreenBmp.Dispose();
                var processed = CaptureHelper.ProcessCapturedBitmap(cropped);

                Dispatcher.Invoke(() =>
                {
                    if (mode == SnipMode.OcrArea)
                    {
                        OcrCaptured?.Invoke(this, processed);
                    }
                    else
                    {
                        AreaCaptured?.Invoke(this, processed);
                    }
                });
            });
        }
        else
        {
            _cachedFullScreen?.Dispose();
            _cachedFullScreen = null;
            NativeMethods.TrimWorkingSet();
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isDragging = false;
            _cachedFullScreen?.Dispose();
            _cachedFullScreen = null;
            Hide();
            NativeMethods.TrimWorkingSet();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cachedFullScreen?.Dispose();
        _cachedFullScreen = null;
        base.OnClosed(e);
    }
}
