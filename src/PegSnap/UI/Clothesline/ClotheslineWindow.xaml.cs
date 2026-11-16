using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PegSnap.Core.Capture;
using PegSnap.Core.Physics;
using PegSnap.Core.Win32;
using PegSnap.UI.Preview;

namespace PegSnap.UI.Clothesline;

public partial class ClotheslineWindow : Window
{
    private class MonitorRopeSpan
    {
        public System.Windows.Forms.Screen Screen { get; set; } = null!;
        public double CanvasLeft { get; set; }
        public double CanvasWidth { get; set; }
        public double CanvasTop { get; set; }
        public int CardCount { get; set; }
    }

    private readonly List<PolaroidCardControl> _cards = new();
    private readonly List<TextCardControl> _textCards = new();
    private readonly Dictionary<TextCardControl, MonitorRopeSpan> _textCardSpans = new();
    private readonly List<MonitorRopeSpan> _screenSpans = new();
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _ropeBounceTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DampedHarmonicOscillator _ropeOscillator = new(18.0, 0.24, 20.0);
    private readonly Stopwatch _ropeStopwatch = new();
    private double _bounceOffset = 0.0;

    private PreviewWindow? _previewWindow;

    public static bool IsDraggingCard { get; set; } = false;

    // Auto-Hide & Clipboard Text Pinning State
    private const int MaxTextCards = 10;
    private bool _isAutoHideEnabled = true;
    private bool _isHidden = true;
    private DateTime _revealHoldUntil = DateTime.MinValue;
    private bool _isAutoPinTextEnabled = true;
    private bool _suppressNextClipboardUpdate = false;

    // Game Overlay & Z-Order Enforcer Fields
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _winEventHook = IntPtr.Zero;
    private NativeMethods.WinEventDelegate? _winEventDelegate;

    public ClotheslineWindow()
    {
        InitializeComponent();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            HideToast();
        };

        _ropeBounceTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _ropeBounceTimer.Tick += OnRopeBounceTick;

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _autoHideTimer.Tick += OnAutoHideTick;
        _autoHideTimer.Start();

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        SetupMultiMonitorVirtualCanvas();
    }

    /// <summary>
    /// Spans the Clothesline across ALL connected monitors seamlessly.
    /// </summary>
    public void SetupMultiMonitorVirtualCanvas()
    {
        // Cover entire virtual desktop across Monitor 1, 2, ... N
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = 360;

        RebuildRopeSpans();
    }

    private void RebuildRopeSpans()
    {
        _screenSpans.Clear();

        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;

        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        // Order screens horizontally from left to right
        var allScreens = System.Windows.Forms.Screen.AllScreens
            .OrderBy(s => s.Bounds.Left)
            .ToList();

        foreach (var screen in allScreens)
        {
            double mLeft = (screen.Bounds.Left / scaleX) - vLeft;
            double mTop = (screen.Bounds.Top / scaleY) - vTop;
            double mWidth = screen.Bounds.Width / scaleX;

            _screenSpans.Add(new MonitorRopeSpan
            {
                Screen = screen,
                CanvasLeft = mLeft,
                CanvasWidth = mWidth,
                CanvasTop = mTop,
                CardCount = 0
            });
        }

        RedrawContinuousRope();
    }

    /// <summary>
    /// Draws the continuous curved rope across all monitors with deep, visible catenary sag.
    /// </summary>
    private void RedrawContinuousRope()
    {
        var geom = new PathGeometry();

        for (int i = 0; i < _screenSpans.Count; i++)
        {
            var span = _screenSpans[i];
            double startX = span.CanvasLeft + 16;
            double endX = span.CanvasLeft + span.CanvasWidth - 16;
            double yAnchor = span.CanvasTop + 18;

            // Deep, realistic 34px catenary sag
            double sagDepth = 34.0 + _bounceOffset;
            double ctrlX = span.CanvasLeft + (span.CanvasWidth / 2.0);
            // In quadratic bezier, midpoint Y drops by (ctrlY - yAnchor) / 2
            // To make the midpoint drop by sagDepth, ctrlY must drop by 2 * sagDepth
            double ctrlY = yAnchor + (2.0 * sagDepth);

            // Each monitor has its own clean, independent curved clothesline
            var figure = new PathFigure { StartPoint = new Point(startX, yAnchor) };
            figure.Segments.Add(new QuadraticBezierSegment(new Point(ctrlX, ctrlY), new Point(endX, yAnchor), isStroked: true));

            geom.Figures.Add(figure);
        }

        RopePath.Data = geom;
    }

    /// <summary>
    /// Calculates the exact Y position on the curved rope for any X coordinate.
    /// </summary>
    public double GetRopeYAtX(double canvasX)
    {
        var span = _screenSpans.FirstOrDefault(s => canvasX >= s.CanvasLeft && canvasX <= s.CanvasLeft + s.CanvasWidth)
                   ?? _screenSpans.FirstOrDefault();

        if (span == null || span.CanvasWidth <= 0) return 22.0;

        double t = Math.Clamp((canvasX - (span.CanvasLeft + 16)) / (span.CanvasWidth - 32), 0.0, 1.0);
        double sagDepth = 34.0 + _bounceOffset;
        // Exact quadratic bezier Y matching ctrlY = yAnchor + 2 * sagDepth:
        return span.CanvasTop + 18.0 + (4.0 * t * (1.0 - t) * sagDepth);
    }

    public void TriggerRopeBounce(double bounceMagnitude = 18.0)
    {
        _ropeOscillator.InitialAngleDegrees = bounceMagnitude;
        _ropeStopwatch.Restart();
        _ropeBounceTimer.Start();
    }

    private void OnRopeBounceTick(object? sender, EventArgs e)
    {
        double elapsed = _ropeStopwatch.Elapsed.TotalSeconds;

        if (_ropeOscillator.IsSettled(elapsed, 0.25))
        {
            _ropeBounceTimer.Stop();
            _ropeStopwatch.Stop();
            _bounceOffset = 0.0;
            RedrawContinuousRope();
            UpdateCardVerticalPositions();
            return;
        }

        _bounceOffset = _ropeOscillator.CalculateAngle(elapsed);
        RedrawContinuousRope();
        UpdateCardVerticalPositions();
    }

    private void UpdateCardVerticalPositions()
    {
        foreach (var card in _cards)
        {
            double cardX = Canvas.GetLeft(card);
            if (!double.IsNaN(cardX))
            {
                double ropeY = GetRopeYAtX(cardX + 78); // center pin
                Canvas.SetTop(card, ropeY - 8); // pin is at Y=8 inside card
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        // Do NOT use WS_EX_NOACTIVATE: It throttles mouse capture events during dragging.
        // Transparent regions pass through via HTTRANSPARENT without activating or stealing focus!
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, 
            exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);

        // Register for event-driven Windows clipboard updates (0% idle CPU)
        NativeMethods.AddClipboardFormatListener(_hwnd);

        // WinEvent Hook: Instantly re-assert Topmost whenever foreground window changes (e.g. FM 26 active/clicked)
        _winEventDelegate = OnForegroundChanged;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS
        );

        EnforceTopmost(force: true);
    }

    /// <summary>
    /// Enforces Clothesline Z-order at the absolute top of the screen (above fullscreen/borderless games like FM 26)
    /// without stealing focus or interrupting game input, with zero flicker.
    /// Triggered event-driven on foreground changes to prevent any blinking.
    /// </summary>
    public void EnforceTopmost(bool force = false)
    {
        // Never interrupt or hitch during an active mouse drag operation
        if (_hwnd == IntPtr.Zero || IsDraggingCard) return;
        try
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOREDRAW);
        }
        catch { }
    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Re-assert topmost immediately when user switches foreground apps (e.g. into or out of FM 26)
        Dispatcher.BeginInvoke(() => EnforceTopmost(force: true), DispatcherPriority.Normal);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoHideTimer.Stop();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
        }
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            Dispatcher.BeginInvoke(HandleClipboardUpdate, DispatcherPriority.Background);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            // Keep focus on the user's active app and deliver the very first click immediately
            handled = true;
            return NativeMethods.MA_NOACTIVATE;
        }

        if (msg == NativeMethods.WM_NCHITTEST)
        {
            // When auto-hidden, let all mouse clicks pass straight through to underlying apps
            if (_isAutoHideEnabled && _isHidden)
            {
                handled = true;
                return NativeMethods.HTTRANSPARENT;
            }

            // Ultra-fast path: While dragging, skip entire visual tree traversal!
            if (IsDraggingCard)
            {
                handled = true;
                return NativeMethods.HTCLIENT;
            }

            int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            try
            {
                var clientPt = PointFromScreen(new Point(x, y));

                bool isInteractive = false;
                VisualTreeHelper.HitTest(this, null, result =>
                {
                    if (result.VisualHit != null)
                    {
                        var dep = result.VisualHit;
                        while (dep != null && dep != this)
                        {
                            if (dep is PolaroidCardControl || dep is TextCardControl || dep is Button || dep is TextBox)
                            {
                                isInteractive = true;
                                return HitTestResultBehavior.Stop;
                            }
                            dep = VisualTreeHelper.GetParent(dep);
                        }
                    }
                    return HitTestResultBehavior.Continue;
                }, new PointHitTestParameters(clientPt));

                if (isInteractive)
                {
                    handled = true;
                    return NativeMethods.HTCLIENT;
                }
                else
                {
                    handled = true;
                    return NativeMethods.HTTRANSPARENT;
                }
            }
            catch { }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Adds a pre-processed screenshot card directly from disk cache + tiny thumbnail.
    /// Zero memory spikes: Raw full-res bitmaps never touch managed memory!
    /// </summary>
    public void AddCapturedScreenshot(string filePath, BitmapSource thumbnail, int origW, int origH, System.Windows.Forms.Screen? screen = null)
    {
        screen ??= System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];

        if (_screenSpans.Count == 0)
        {
            RebuildRopeSpans();
        }

        var span = _screenSpans.FirstOrDefault(s => s.Screen.DeviceName == screen.DeviceName)
                   ?? _screenSpans.FirstOrDefault()
                   ?? new MonitorRopeSpan { CanvasLeft = 0, CanvasWidth = Width, CanvasTop = 0 };

        var card = new PolaroidCardControl();
        card.SetImageFromCapture(filePath, thumbnail, origW, origH);

        double cardSpacing = 162.0;
        double startX = span.CanvasLeft + 35.0 + (span.CardCount * cardSpacing);

        if (startX + 154 > span.CanvasLeft + span.CanvasWidth - 30)
        {
            startX = span.CanvasLeft + 35.0 + ((span.CardCount % 4) * cardSpacing);
        }

        double ropeY = GetRopeYAtX(startX + 78);

        Canvas.SetLeft(card, startX);
        Canvas.SetTop(card, ropeY - 8);

        span.CardCount++;

        card.RemoveRequested += (s, e) => RemoveCard(card, span);
        card.PreviewRequested += (s, e) => ShowPreview(card);
        card.CopiedToClipboard += (s, e) => ShowToast("📋", "Copied to Clipboard!");

        card.PositionChanged += (s, newX) =>
        {
            double curRopeY = GetRopeYAtX(newX + 78);
            card.UpdateDragVertical(curRopeY - 8);
        };

        card.DragFinished += (s, finalX) =>
        {
            TriggerRopeBounce(6.0);
        };

        _cards.Add(card);
        CardsCanvas.Children.Add(card);

        card.AnimateEntrance();
        TriggerRopeBounce(18.0);

        UpdateControlsVisibility();
        Show();
        RevealTemporarily(2200);
        EnforceTopmost(force: true);
    }

    /// <summary>
    /// Adds a screenshot card to the specific monitor section of the continuous rope!
    /// </summary>
    public void AddScreenshot(BitmapSource image, System.Windows.Forms.Screen? screen = null)
    {
        screen ??= System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];

        if (_screenSpans.Count == 0)
        {
            RebuildRopeSpans();
        }

        var span = _screenSpans.FirstOrDefault(s => s.Screen.DeviceName == screen.DeviceName)
                   ?? _screenSpans.FirstOrDefault()
                   ?? new MonitorRopeSpan { CanvasLeft = 0, CanvasWidth = Width, CanvasTop = 0 };

        var card = new PolaroidCardControl();
        card.SetImage(image);

        // Position on the designated screen section of the rope
        double cardSpacing = 162.0;
        double startX = span.CanvasLeft + 35.0 + (span.CardCount * cardSpacing);

        // If cards exceed screen width, wrap or stagger
        if (startX + 154 > span.CanvasLeft + span.CanvasWidth - 30)
        {
            startX = span.CanvasLeft + 35.0 + ((span.CardCount % 4) * cardSpacing);
        }

        double ropeY = GetRopeYAtX(startX + 78);

        Canvas.SetLeft(card, startX);
        Canvas.SetTop(card, ropeY - 8);

        span.CardCount++;

        card.RemoveRequested += (s, e) => RemoveCard(card, span);
        card.PreviewRequested += (s, e) => ShowPreview(card);
        card.CopiedToClipboard += (s, e) => ShowToast("📋", "Copied to Clipboard!");
        
        // 1:1 Instant Zero-Lag Position Updates (Pure GPU Transforms, 0 WPF Layout Passes)
        card.PositionChanged += (s, newX) =>
        {
            double curRopeY = GetRopeYAtX(newX + 78);
            card.UpdateDragVertical(curRopeY - 8);
        };

        // Spring sag bounce ONLY when dragging finishes and card is released!
        card.DragFinished += (s, finalX) =>
        {
            TriggerRopeBounce(6.0);
        };

        _cards.Add(card);
        CardsCanvas.Children.Add(card);

        card.AnimateEntrance();
        TriggerRopeBounce(18.0); // Deep spring sag bounce!

        UpdateControlsVisibility();
        Show();
        RevealTemporarily(2200);
        EnforceTopmost();
    }

    private void RemoveCard(PolaroidCardControl card, MonitorRopeSpan? span)
    {
        var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
        fadeAnim.Completed += (s, e) =>
        {
            CardsCanvas.Children.Remove(card);
            _cards.Remove(card);
            if (span != null && span.CardCount > 0) span.CardCount--;

            if (_previewWindow != null && _previewWindow.IsLoaded)
            {
                if (_cards.Count == 0)
                {
                    _previewWindow.Close();
                }
                else
                {
                    if (_currentPreviewIndex >= _cards.Count) _currentPreviewIndex = _cards.Count - 1;
                    UpdatePreviewContent();
                }
            }

            UpdateControlsVisibility();
            TriggerRopeBounce(8.0);
            App.ScheduleWorkingSetTrim(600);
        };
        card.BeginAnimation(OpacityProperty, fadeAnim);
    }

    private void UpdateControlsVisibility()
    {
        int count = _cards.Count + _textCards.Count;
        if (count > 0)
        {
            CountBadge.Visibility = Visibility.Visible;
            CountText.Text = $"{count} snap{(count > 1 ? "s" : "")}";
            ClearAllBtn.Visibility = Visibility.Visible;
        }
        else
        {
            CountBadge.Visibility = Visibility.Collapsed;
            ClearAllBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        ClearAll();
    }

    public void ClearAll()
    {
        if (_previewWindow != null && _previewWindow.IsLoaded)
        {
            _previewWindow.Close();
        }

        CardsCanvas.Children.Clear();
        _cards.Clear();
        _textCards.Clear();
        _textCardSpans.Clear();
        foreach (var span in _screenSpans) span.CardCount = 0;

        UpdateControlsVisibility();
        TriggerRopeBounce(12.0);
        ShowToast("🧺", "Clothesline cleared!");
        App.ScheduleWorkingSetTrim(400);
    }

    public void ShowToast(string icon, string message)
    {
        RevealTemporarily(2500);
        ToastIcon.Text = icon;
        ToastMessage.Text = message;
        ToastBanner.Visibility = Visibility.Visible;

        // Dynamically center banner on primary monitor
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Canvas.SetLeft(ToastBanner, Math.Max(20, (screenWidth - 280) / 2.0));

        var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
        ToastBanner.BeginAnimation(OpacityProperty, anim);

        _toastTimer.Stop();
        _toastTimer.Start();
        EnforceTopmost(force: true);
    }

    private void HideToast()
    {
        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (s, e) => ToastBanner.Visibility = Visibility.Collapsed;
        ToastBanner.BeginAnimation(OpacityProperty, anim);
    }

    private int _currentPreviewIndex = -1;

    private void ShowPreview(PolaroidCardControl card)
    {
        _currentPreviewIndex = _cards.IndexOf(card);
        if (_currentPreviewIndex < 0) _currentPreviewIndex = 0;

        if (_previewWindow == null || !_previewWindow.IsLoaded)
        {
            _previewWindow = new PreviewWindow();
            _previewWindow.Owner = this;
            _previewWindow.NavigateRequested += (s, dir) => NavigatePreview(dir);
            _previewWindow.Closed += (s, e) =>
            {
                _previewWindow = null;
                App.ScheduleWorkingSetTrim(500);
            };
        }

        if (_previewWindow.WindowState == WindowState.Minimized)
        {
            _previewWindow.WindowState = WindowState.Normal;
        }

        UpdatePreviewContent();
        _previewWindow.Show();
        _previewWindow.Activate();
    }

    private void NavigatePreview(int direction)
    {
        if (_cards.Count == 0) return;
        _currentPreviewIndex += direction;
        if (_currentPreviewIndex < 0) _currentPreviewIndex = _cards.Count - 1;
        if (_currentPreviewIndex >= _cards.Count) _currentPreviewIndex = 0;

        UpdatePreviewContent();
    }

    private void UpdatePreviewContent()
    {
        if (_previewWindow != null && _currentPreviewIndex >= 0 && _currentPreviewIndex < _cards.Count)
        {
            var card = _cards[_currentPreviewIndex];
            _previewWindow.SetImage(card.OriginalImage, _currentPreviewIndex + 1, _cards.Count);
        }
    }

    #region Auto-Hide Top Edge

    public void SetAutoHideEnabled(bool enabled)
    {
        _isAutoHideEnabled = enabled;
        if (!enabled)
        {
            _autoHideTimer.Stop();
            SetAutoHideState(false);
            AutoHideHandle.Opacity = 0;
        }
        else
        {
            _autoHideTimer.Start();
            _revealHoldUntil = DateTime.UtcNow.AddMilliseconds(1200);
        }
    }

    public void RevealTemporarily(int durationMs = 2200)
    {
        if (!_isAutoHideEnabled) return;
        _revealHoldUntil = DateTime.UtcNow.AddMilliseconds(durationMs);
        SetAutoHideState(false);

        // Subtle blue accent pulse on top-edge handle
        AutoHideHandle.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x84, 0xFF));
        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        resetTimer.Tick += (s, e) =>
        {
            resetTimer.Stop();
            AutoHideHandle.Background = new SolidColorBrush(Color.FromArgb(0x90, 0xE5, 0xE5, 0xEA));
        };
        resetTimer.Start();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        if (!_isAutoHideEnabled || !IsLoaded) return;

        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        int spanTopPx = 0;
        var activeSpan = _screenSpans.FirstOrDefault(s => pt.X >= s.Screen.Bounds.Left && pt.X < s.Screen.Bounds.Right);
        if (activeSpan != null)
        {
            spanTopPx = activeSpan.Screen.Bounds.Top;
        }
        else if (_screenSpans.Count > 0)
        {
            spanTopPx = _screenSpans[0].Screen.Bounds.Top;
        }

        double dipYFromTop = (pt.Y - spanTopPx) / scaleY;

        if (_isHidden)
        {
            if (dipYFromTop <= 4.0)
            {
                SetAutoHideState(false);
            }
        }
        else
        {
            bool keepOpen = IsDraggingCard
                || (_previewWindow != null && _previewWindow.IsLoaded)
                || DateTime.UtcNow < _revealHoldUntil
                || dipYFromTop <= 235.0;

            if (!keepOpen)
            {
                SetAutoHideState(true);
            }
        }
    }

    private void SetAutoHideState(bool hide)
    {
        if (_isHidden == hide) return;
        _isHidden = hide;

        double targetY = hide ? -360.0 : 0.0;
        var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(hide ? 200 : 180))
        {
            EasingFunction = hide
                ? new CubicEase { EasingMode = EasingMode.EaseIn }
                : new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        AutoHideTransform.BeginAnimation(TranslateTransform.YProperty, anim);

        double handleOpacity = (hide && _isAutoHideEnabled) ? 0.45 : 0.0;
        AutoHideHandle.BeginAnimation(OpacityProperty, new DoubleAnimation(handleOpacity, TimeSpan.FromMilliseconds(160)));
    }

    #endregion

    #region Auto-Pin Copied Text Cards

    public void SetAutoPinTextEnabled(bool enabled)
    {
        _isAutoPinTextEnabled = enabled;
    }

    private void HandleClipboardUpdate()
    {
        if (!_isAutoPinTextEnabled) return;

        if (_suppressNextClipboardUpdate)
        {
            _suppressNextClipboardUpdate = false;
            return;
        }

        try
        {
            if (!Clipboard.ContainsText()) return;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Anti-duplicate filter: skip if identical text is already pinned
            if (_textCards.Any(c => string.Equals(c.ClipText, text, StringComparison.Ordinal)))
            {
                return;
            }

            AddTextClip(text);
        }
        catch
        {
            // Clipboard may be briefly locked by another process; safely ignore
        }
    }

    public void AddTextClip(string text)
    {
        if (_screenSpans.Count == 0)
        {
            RebuildRopeSpans();
        }

        // Enforce maximum of 10 text cards (evict oldest first)
        while (_textCards.Count >= MaxTextCards)
        {
            RemoveTextCard(_textCards[0], animate: false);
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
        var span = _screenSpans.FirstOrDefault(s => s.Screen.DeviceName == screen.DeviceName)
                   ?? _screenSpans.FirstOrDefault()
                   ?? new MonitorRopeSpan { CanvasLeft = 0, CanvasWidth = Width, CanvasTop = 0 };

        var card = new TextCardControl();
        card.SetText(text);

        double cardSpacing = 162.0;
        double startX = span.CanvasLeft + 35.0 + (span.CardCount * cardSpacing);
        if (startX + 154 > span.CanvasLeft + span.CanvasWidth - 30)
        {
            startX = span.CanvasLeft + 35.0 + ((span.CardCount % 4) * cardSpacing);
        }

        double ropeY = GetRopeYAtX(startX + 78);
        Canvas.SetLeft(card, startX);
        Canvas.SetTop(card, ropeY - 8);

        span.CardCount++;
        _textCardSpans[card] = span;

        card.CardRemoved += (s, e) => RemoveTextCard(card, animate: true);
        card.CardCopied += (s, copiedText) =>
        {
            try
            {
                _suppressNextClipboardUpdate = true;
                System.Windows.Forms.Clipboard.SetText(copiedText);
                ShowToast("📋", "Text copied to Clipboard!");
            }
            catch
            {
                _suppressNextClipboardUpdate = false;
            }
        };
        card.DragStateChanged += (s, dragging) => IsDraggingCard = dragging;
        card.PositionChanged += (s, centerX) =>
        {
            double curRopeY = GetRopeYAtX(centerX);
            Canvas.SetTop(card, curRopeY - 8);
        };

        _textCards.Add(card);
        CardsCanvas.Children.Add(card);

        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180)));
        card.ApplyImpulse(8.0);
        TriggerRopeBounce(10.0);

        UpdateControlsVisibility();
        Show();
        RevealTemporarily(2000);
        EnforceTopmost();
    }

    private void RemoveTextCard(TextCardControl card, bool animate)
    {
        if (!_textCards.Contains(card)) return;

        void Cleanup()
        {
            CardsCanvas.Children.Remove(card);
            _textCards.Remove(card);
            if (_textCardSpans.TryGetValue(card, out var span))
            {
                if (span.CardCount > 0) span.CardCount--;
                _textCardSpans.Remove(card);
            }
            UpdateControlsVisibility();
        }

        if (!animate)
        {
            Cleanup();
            return;
        }

        var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
        fadeAnim.Completed += (s, e) =>
        {
            Cleanup();
            TriggerRopeBounce(6.0);
        };
        card.BeginAnimation(OpacityProperty, fadeAnim);
    }

    #endregion
}
