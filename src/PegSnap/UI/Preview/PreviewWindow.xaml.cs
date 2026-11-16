using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PegSnap.Core.Capture;
using PegSnap.Core.Ocr;
using PegSnap.Core.Win32;
using IOPath = System.IO.Path;

namespace PegSnap.UI.Preview;

public partial class PreviewWindow : Window
{
    public event EventHandler<int>? NavigateRequested;

    private BitmapSource? _currentImage;
    private double _currentZoom = 1.0;
    private double _fitZoom = 1.0;
    private Point _panStartMouse;
    private double _startPanX;
    private double _startPanY;
    private bool _isPanning;
    private readonly DispatcherTimer _toastTimer;

    private enum AnnotationTool { Pan, Pen, Line, Rect, Erase }
    private AnnotationTool _activeTool = AnnotationTool.Pan;
    private Color _activeColor = Color.FromRgb(0xFF, 0x45, 0x3A);
    private double _strokeThickness = 5.0;
    private bool _isDrawing;
    private Point _drawStartPoint;
    private Polyline? _activePolyline;
    private Line? _activeLine;
    private Rectangle? _activeRect;
    private readonly Stack<UIElement> _undoStack = new();

    public PreviewWindow()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            HideNotification();
        };

        KeyDown += OnWindowKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCHITTEST)
        {
            int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            try
            {
                var pt = PointFromScreen(new Point(screenX, screenY));

                // Visual card is within Margin="16"
                double left = 16.0;
                double top = 16.0;
                double right = ActualWidth - 16.0;
                double bottom = ActualHeight - 16.0;
                const double resizeBorder = 12.0;

                // Check 8-way resize bounds around the perimeter of the visual card
                if (pt.X >= left - 8.0 && pt.X <= right + 8.0 && pt.Y >= top - 8.0 && pt.Y <= bottom + 8.0)
                {
                    bool isLeft = pt.X <= left + resizeBorder;
                    bool isRight = pt.X >= right - resizeBorder;
                    bool isTop = pt.Y <= top + resizeBorder;
                    bool isBottom = pt.Y >= bottom - resizeBorder;

                    if (isTop && isLeft) { handled = true; return NativeMethods.HTTOPLEFT; }
                    if (isTop && isRight) { handled = true; return NativeMethods.HTTOPRIGHT; }
                    if (isBottom && isLeft) { handled = true; return NativeMethods.HTBOTTOMLEFT; }
                    if (isBottom && isRight) { handled = true; return NativeMethods.HTBOTTOMRIGHT; }

                    if (isLeft) { handled = true; return NativeMethods.HTLEFT; }
                    if (isRight) { handled = true; return NativeMethods.HTRIGHT; }
                    if (isTop) { handled = true; return NativeMethods.HTTOP; }
                    if (isBottom) { handled = true; return NativeMethods.HTBOTTOM; }
                }
            }
            catch { }
        }

        return IntPtr.Zero;
    }

    public void SetImage(BitmapSource bitmap, int currentIndex = 0, int totalCount = 0)
    {
        _currentImage = bitmap;
        PreviewImage.Source = bitmap;
        PreviewImage.Width = bitmap.PixelWidth;
        PreviewImage.Height = bitmap.PixelHeight;
        AnnotationCanvas.Width = bitmap.PixelWidth;
        AnnotationCanvas.Height = bitmap.PixelHeight;
        AnnotationCanvas.Children.Clear();
        _undoStack.Clear();
        DimensionsText.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px";

        if (totalCount > 1)
        {
            IndexCounterText.Text = $"{currentIndex} of {totalCount}";
            IndexCounterText.Visibility = Visibility.Visible;
            IndexSeparator.Visibility = Visibility.Visible;
            PrevButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;
        }
        else
        {
            IndexCounterText.Visibility = Visibility.Collapsed;
            IndexSeparator.Visibility = Visibility.Collapsed;
            PrevButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
        }

        // Smart Dynamic Sizing: Adapt window dimensions to image aspect ratio
        AdjustWindowSizeForImage(bitmap);

        // Auto-fit image to viewport on load
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ZoomToFit();
        }), DispatcherPriority.Loaded);
    }

    private void AdjustWindowSizeForImage(BitmapSource bitmap)
    {
        var workArea = SystemParameters.WorkArea;
        double maxW = workArea.Width * 0.85;
        double maxH = workArea.Height * 0.82;
        double minW = 600;
        double minH = 440;

        double aspect = (double)bitmap.PixelWidth / Math.Max(1, bitmap.PixelHeight);

        double targetW = Math.Clamp(bitmap.PixelWidth + 64, minW, maxW);
        double targetH = targetW / aspect;

        if (targetH > maxH)
        {
            targetH = maxH;
            targetW = targetH * aspect;
        }

        double winW = Math.Clamp(targetW + 32, minW, maxW);
        double winH = Math.Clamp(targetH + 46 + 32, minH, maxH);

        Width = winW;
        Height = winH;

        Left = workArea.Left + (workArea.Width - winW) / 2;
        Top = workArea.Top + (workArea.Height - winH) / 2;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => Close();

    private void OnZoomToggleClicked(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Space)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopyImageToClipboard();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            UndoAnnotation();
            e.Handled = true;
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveImageToFile();
            e.Handled = true;
        }
        else if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ResetZoom();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            NavigateRequested?.Invoke(this, -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            NavigateRequested?.Invoke(this, 1);
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            ZoomToFit();
            e.Handled = true;
        }
    }

    #region Zoom & Pan Controls

    public void ZoomToFit()
    {
        if (_currentImage == null) return;

        double availW = ViewportContainer.ActualWidth;
        double availH = ViewportContainer.ActualHeight;

        if (availW <= 0 || availH <= 0)
        {
            availW = Math.Max(200, ActualWidth - 64);
            availH = Math.Max(200, ActualHeight - 46 - 64);
        }

        // 32px breathing room padding
        availW = Math.Max(100, availW - 32);
        availH = Math.Max(100, availH - 32);

        double scaleX = availW / _currentImage.PixelWidth;
        double scaleY = availH / _currentImage.PixelHeight;

        _fitZoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.05, 5.0);

        SetZoom(_fitZoom, isFit: true);

        ImagePan.X = 0;
        ImagePan.Y = 0;
    }

    private void ResetZoom()
    {
        ZoomToFit();
    }

    private void SetZoom(double zoom, bool isFit = false)
    {
        _currentZoom = Math.Clamp(zoom, 0.05, 5.0);
        ImageScale.ScaleX = _currentZoom;
        ImageScale.ScaleY = _currentZoom;

        int pct = (int)Math.Round(_currentZoom * 100);
        if (isFit || Math.Abs(_currentZoom - _fitZoom) < 0.02)
        {
            ZoomLevelText.Text = $"Fit ({pct}%)";
        }
        else
        {
            ZoomLevelText.Text = $"{pct}%";
        }
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => ZoomAtCenter(0.25);

    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => ZoomAtCenter(-0.25);

    private void OnZoomFitClicked(object sender, RoutedEventArgs e) => ZoomToFit();

    private void ZoomAtCenter(double delta)
    {
        double newZoom = Math.Clamp(_currentZoom + delta, 0.05, 5.0);
        if (Math.Abs(newZoom - _currentZoom) < 0.001) return;

        double ratio = newZoom / _currentZoom;
        ImagePan.X *= ratio;
        ImagePan.Y *= ratio;
        SetZoom(newZoom);
    }

    private void OnPrevClicked(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(this, -1);

    private void OnNextClicked(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(this, 1);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_currentImage != null && Math.Abs(_currentZoom - _fitZoom) < 0.03)
        {
            ZoomToFit();
        }
    }

    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        double delta = e.Delta > 0 ? 0.15 : -0.15;
        double newZoom = Math.Clamp(_currentZoom + delta, 0.05, 5.0);
        if (Math.Abs(newZoom - _currentZoom) < 0.001) return;

        if (ViewportContainer.ActualWidth > 0 && ViewportContainer.ActualHeight > 0)
        {
            Point mousePos = e.GetPosition(ViewportContainer);
            double cx = mousePos.X - ViewportContainer.ActualWidth / 2.0;
            double cy = mousePos.Y - ViewportContainer.ActualHeight / 2.0;

            double ratio = newZoom / _currentZoom;
            ImagePan.X = ImagePan.X * ratio + cx * (1.0 - ratio);
            ImagePan.Y = ImagePan.Y * ratio + cy * (1.0 - ratio);
        }

        SetZoom(newZoom);
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        OnWindowPreviewMouseWheel(sender, e);
    }

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != AnnotationTool.Pan)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pt = e.GetPosition(AnnotationCanvas);
            _isDrawing = true;
            _drawStartPoint = pt;
            ViewportContainer.CaptureMouse();

            if (_activeTool == AnnotationTool.Erase)
            {
                EraseAtPoint(pt);
                return;
            }

            var brush = new SolidColorBrush(_activeColor);
            brush.Freeze();

            if (_activeTool == AnnotationTool.Pen)
            {
                _activePolyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                _activePolyline.Points.Add(pt);
                AnnotationCanvas.Children.Add(_activePolyline);
            }
            else if (_activeTool == AnnotationTool.Line)
            {
                _activeLine = new Line
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    X1 = pt.X,
                    Y1 = pt.Y,
                    X2 = pt.X,
                    Y2 = pt.Y
                };
                AnnotationCanvas.Children.Add(_activeLine);
            }
            else if (_activeTool == AnnotationTool.Rect)
            {
                _activeRect = new Rectangle
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    RadiusX = 3,
                    RadiusY = 3,
                    Width = 0,
                    Height = 0
                };
                Canvas.SetLeft(_activeRect, pt.X);
                Canvas.SetTop(_activeRect, pt.Y);
                AnnotationCanvas.Children.Add(_activeRect);
            }
            return;
        }

        if (e.ClickCount == 2)
        {
            // Double click toggles between Fit and 100% 1:1 Pixel-Perfect
            if (Math.Abs(_currentZoom - 1.0) < 0.05)
                ZoomToFit();
            else
            {
                SetZoom(1.0);
                ImagePan.X = 0;
                ImagePan.Y = 0;
            }
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStartMouse = e.GetPosition(this);
            _startPanX = ImagePan.X;
            _startPanY = ImagePan.Y;
            ViewportContainer.CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    private void OnImageMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDrawing)
        {
            var pt = e.GetPosition(AnnotationCanvas);
            if (_activeTool == AnnotationTool.Erase)
            {
                EraseAtPoint(pt);
            }
            else if (_activeTool == AnnotationTool.Pen && _activePolyline != null)
            {
                _activePolyline.Points.Add(pt);
            }
            else if (_activeTool == AnnotationTool.Line && _activeLine != null)
            {
                _activeLine.X2 = pt.X;
                _activeLine.Y2 = pt.Y;
            }
            else if (_activeTool == AnnotationTool.Rect && _activeRect != null)
            {
                double x = Math.Min(_drawStartPoint.X, pt.X);
                double y = Math.Min(_drawStartPoint.Y, pt.Y);
                double w = Math.Abs(pt.X - _drawStartPoint.X);
                double h = Math.Abs(pt.Y - _drawStartPoint.Y);
                Canvas.SetLeft(_activeRect, x);
                Canvas.SetTop(_activeRect, y);
                _activeRect.Width = w;
                _activeRect.Height = h;
            }
            return;
        }

        if (_isPanning)
        {
            Point current = e.GetPosition(this);
            double deltaX = current.X - _panStartMouse.X;
            double deltaY = current.Y - _panStartMouse.Y;

            ImagePan.X = _startPanX + deltaX;
            ImagePan.Y = _startPanY + deltaY;
        }
    }

    private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            ViewportContainer.ReleaseMouseCapture();

            if (_activePolyline != null)
            {
                if (_activePolyline.Points.Count > 1) _undoStack.Push(_activePolyline);
                else AnnotationCanvas.Children.Remove(_activePolyline);
                _activePolyline = null;
            }
            else if (_activeLine != null)
            {
                double dx = _activeLine.X2 - _activeLine.X1;
                double dy = _activeLine.Y2 - _activeLine.Y1;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 3.0) _undoStack.Push(_activeLine);
                else AnnotationCanvas.Children.Remove(_activeLine);
                _activeLine = null;
            }
            else if (_activeRect != null)
            {
                if (_activeRect.Width > 3.0 || _activeRect.Height > 3.0) _undoStack.Push(_activeRect);
                else AnnotationCanvas.Children.Remove(_activeRect);
                _activeRect = null;
            }
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            ViewportContainer.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void OnImageRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ZoomToFit();
    }

    #endregion

    #region Action Buttons & In-Window Notifications

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        CopyImageToClipboard();
    }

    private void CopyImageToClipboard()
    {
        var bmp = GetAnnotatedBitmap();
        if (bmp == null) return;
        CaptureHelper.CopyToClipboard(bmp);
        ShowNotification("✓", "Copied to Clipboard");
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        SaveImageToFile();
    }

    private void SaveImageToFile()
    {
        if (_currentImage == null) return;

        var saveDialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
            DefaultExt = ".png",
            FileName = $"Clothey_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (saveDialog.ShowDialog(this) == true)
        {
            try
            {
                var bmp = GetAnnotatedBitmap() ?? _currentImage;
                using var fs = new FileStream(saveDialog.FileName, FileMode.Create);
                BitmapEncoder encoder = IOPath.GetExtension(saveDialog.FileName).ToLower() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);

                ShowNotification("✓", "Image Saved Successfully");
            }
            catch (Exception ex)
            {
                ShowNotification("⚠️", $"Save failed: {ex.Message}");
            }
        }
    }

    private async void OnOcrClicked(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        try
        {
            ShowNotification("⏳", "Scanning Text with OCR...");
            string extracted = await OcrService.ExtractTextAsync(_currentImage);

            if (string.IsNullOrWhiteSpace(extracted))
            {
                ShowNotification("ℹ️", "No readable text detected");
                return;
            }

            Clipboard.SetText(extracted);
            ShowNotification("✓", $"OCR Text Copied ({extracted.Length} chars)");
        }
        catch (Exception ex)
        {
            ShowNotification("⚠️", $"OCR Error: {ex.Message}");
        }
    }

    private void OnOpenInAppClicked(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        try
        {
            var bmp = GetAnnotatedBitmap() ?? _currentImage;
            string tempPath = IOPath.Combine(IOPath.GetTempPath(), $"Clothey_Open_{Guid.NewGuid():N}.png");
            using (var fs = new FileStream(tempPath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            ShowNotification("↗", "Opened in Default Viewer");
        }
        catch (Exception ex)
        {
            ShowNotification("⚠️", $"Could not open: {ex.Message}");
        }
    }

    public void ShowNotification(string icon, string message)
    {
        NotificationIcon.Text = icon;
        NotificationText.Text = message;
        NotificationPill.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        NotificationPill.BeginAnimation(OpacityProperty, fadeIn);

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideNotification()
    {
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, e) => NotificationPill.Visibility = Visibility.Collapsed;
        NotificationPill.BeginAnimation(OpacityProperty, fadeOut);
    }

    #endregion

    #region Annotation Tools & Bitmap Flattening

    private void OnToolSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        _activeTool = tag switch
        {
            "Pen" => AnnotationTool.Pen,
            "Line" => AnnotationTool.Line,
            "Rect" => AnnotationTool.Rect,
            "Erase" => AnnotationTool.Erase,
            _ => AnnotationTool.Pan
        };

        ViewportContainer.Cursor = _activeTool == AnnotationTool.Pan ? Cursors.Hand : Cursors.Cross;

        var activeBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        var idleBg = Brushes.Transparent;

        SetToolButtonHighlight(ToolPanBtn, _activeTool == AnnotationTool.Pan ? activeBg : idleBg);
        SetToolButtonHighlight(ToolPenBtn, _activeTool == AnnotationTool.Pen ? activeBg : idleBg);
        SetToolButtonHighlight(ToolLineBtn, _activeTool == AnnotationTool.Line ? activeBg : idleBg);
        SetToolButtonHighlight(ToolRectBtn, _activeTool == AnnotationTool.Rect ? activeBg : idleBg);
        SetToolButtonHighlight(ToolEraseBtn, _activeTool == AnnotationTool.Erase ? activeBg : idleBg);
    }

    private static void SetToolButtonHighlight(Button btn, Brush bg)
    {
        if (btn.Template?.FindName("Brd", btn) is Border brd)
        {
            brd.Background = bg;
        }
    }

    private void OnColorSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string hex) return;
        _activeColor = (Color)ColorConverter.ConvertFromString(hex);

        Button[] swatches = { ColorRedBtn, ColorYellowBtn, ColorBlueBtn, ColorGreenBtn, ColorWhiteBtn };
        foreach (var sw in swatches)
        {
            if (VisualTreeHelper.GetChildrenCount(sw) > 0 && VisualTreeHelper.GetChild(sw, 0) is Border brd)
            {
                brd.BorderBrush = sw == btn ? Brushes.White : Brushes.Transparent;
            }
        }
    }

    private void OnCycleStrokeWidth(object sender, RoutedEventArgs e)
    {
        if (_strokeThickness < 4.0)
        {
            _strokeThickness = 5.0;
            StrokeWidthBtn.Content = "M";
        }
        else if (_strokeThickness < 7.0)
        {
            _strokeThickness = 9.0;
            StrokeWidthBtn.Content = "L";
        }
        else
        {
            _strokeThickness = 3.0;
            StrokeWidthBtn.Content = "S";
        }
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e) => UndoAnnotation();

    private void UndoAnnotation()
    {
        while (_undoStack.Count > 0)
        {
            var top = _undoStack.Pop();
            if (AnnotationCanvas.Children.Contains(top))
            {
                AnnotationCanvas.Children.Remove(top);
                break;
            }
        }
    }

    private void OnClearAnnotationsClicked(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Children.Clear();
        _undoStack.Clear();
        ShowNotification("🗑", "Annotations Cleared");
    }

    private void EraseAtPoint(Point pt)
    {
        const double hitRadius = 14.0;
        UIElement? toRemove = null;

        for (int i = AnnotationCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = AnnotationCanvas.Children[i];
            if (child is Line line)
            {
                if (DistanceToSegment(pt, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2)) <= hitRadius + line.StrokeThickness / 2.0)
                {
                    toRemove = child;
                    break;
                }
            }
            else if (child is Rectangle rect)
            {
                double x = Canvas.GetLeft(rect);
                double y = Canvas.GetTop(rect);
                var outer = new Rect(x - hitRadius, y - hitRadius, rect.Width + hitRadius * 2, rect.Height + hitRadius * 2);
                var inner = new Rect(x + hitRadius, y + hitRadius, Math.Max(0, rect.Width - hitRadius * 2), Math.Max(0, rect.Height - hitRadius * 2));
                if (outer.Contains(pt) && !inner.Contains(pt))
                {
                    toRemove = child;
                    break;
                }
            }
            else if (child is Polyline poly)
            {
                for (int j = 1; j < poly.Points.Count; j++)
                {
                    if (DistanceToSegment(pt, poly.Points[j - 1], poly.Points[j]) <= hitRadius + poly.StrokeThickness / 2.0)
                    {
                        toRemove = child;
                        break;
                    }
                }
                if (toRemove != null) break;
            }
        }

        if (toRemove != null)
        {
            AnnotationCanvas.Children.Remove(toRemove);
        }
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        if (dx == 0 && dy == 0)
        {
            double px = p.X - a.X;
            double py = p.Y - a.Y;
            return Math.Sqrt(px * px + py * py);
        }
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0.0, 1.0);
        double rx = p.X - (a.X + t * dx);
        double ry = p.Y - (a.Y + t * dy);
        return Math.Sqrt(rx * rx + ry * ry);
    }

    private BitmapSource? GetAnnotatedBitmap()
    {
        if (_currentImage == null) return null;
        if (AnnotationCanvas.Children.Count == 0) return _currentImage;

        int w = _currentImage.PixelWidth;
        int h = _currentImage.PixelHeight;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_currentImage, new Rect(0, 0, w, h));
            var vb = new VisualBrush(AnnotationCanvas)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    #endregion
}
