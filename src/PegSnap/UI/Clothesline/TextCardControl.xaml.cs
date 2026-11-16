using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PegSnap.Core.Physics;

namespace PegSnap.UI.Clothesline;

public partial class TextCardControl : UserControl
{
    private readonly DampedHarmonicOscillator _oscillator = new(18.0, 0.22, 14.0);
    private readonly Stopwatch _physicsStopwatch = new();
    private readonly DispatcherTimer _physicsTimer;

    private bool _isPointerDown;
    private bool _isDragging;
    private double _grabOffsetX;
    private Point _pressStartOnCanvas;
    private double _initialCardLeft;
    private double _initialCardTop;
    private double _lastLeft;
    private const double DragThreshold = 7.0;

    public string ClipText { get; private set; } = string.Empty;

    public event EventHandler? CardRemoved;
    public event EventHandler<string>? CardCopied;
    public event EventHandler<double>? PositionChanged;
    public event EventHandler<bool>? DragStateChanged;

    public TextCardControl()
    {
        InitializeComponent();

        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _physicsTimer.Tick += OnPhysicsTick;

        MouseDown += OnCardMouseDown;
        MouseMove += OnCardMouseMove;
        MouseUp += OnCardMouseUp;
    }

    public void SetText(string text)
    {
        ClipText = text;
        string trimmed = text.Trim();

        string normalized = trimmed.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        PreviewTextBlock.Text = normalized.Length > 140 ? normalized[..140] + "…" : normalized;
        CharCountText.Text = $"{text.Length} ch";
        ToolTip = trimmed.Length > 500 ? trimmed[..500] + "…" : trimmed;

        ApplyCategoryBadge(trimmed);
    }

    private void ApplyCategoryBadge(string trimmed)
    {
        ColorSwatch.Visibility = Visibility.Collapsed;
        PreviewTextBlock.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");

        // 1. Hex Color (#RGB, #RRGGBB, #AARRGGBB)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(trimmed);
                ColorSwatch.Background = new SolidColorBrush(color);
                ColorSwatch.Visibility = Visibility.Visible;
                SetBadgeStyle("COLOR", Color.FromRgb(0xFF, 0xD6, 0x0A));
                PreviewTextBlock.FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI");
                return;
            }
            catch { }
        }

        // 2. URL
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SetBadgeStyle("URL", Color.FromRgb(0x5A, 0xC8, 0xFA));
            return;
        }

        // 3. Code snippet heuristic
        if (trimmed.Contains('{') && trimmed.Contains('}') ||
            trimmed.Contains("=>") ||
            trimmed.Contains("function ") ||
            trimmed.Contains("public ") ||
            trimmed.Contains("private ") ||
            trimmed.Contains("const ") ||
            trimmed.Contains("import ") ||
            trimmed.Contains("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            SetBadgeStyle("CODE", Color.FromRgb(0xBF, 0x5A, 0xF2));
            PreviewTextBlock.FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI");
            return;
        }

        // 4. Default plain text
        SetBadgeStyle("CLIP", Color.FromRgb(0x5A, 0xC8, 0xFA));
    }

    private void SetBadgeStyle(string label, Color accent)
    {
        CategoryText.Text = label;
        CategoryText.Foreground = new SolidColorBrush(accent);
        CategoryBadge.Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));
        CategoryBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x48, accent.R, accent.G, accent.B));
    }

    public void ApplyImpulse(double impulse)
    {
        _oscillator.InitialAngleDegrees = Math.Clamp(impulse, -22.0, 22.0);
        _physicsStopwatch.Restart();
        if (!_physicsTimer.IsEnabled)
        {
            _physicsTimer.Start();
        }
    }

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        double t = _physicsStopwatch.Elapsed.TotalSeconds;
        CardRotation.Angle = _oscillator.CalculateAngle(t);

        if (!_isDragging && _oscillator.IsSettled(t))
        {
            CardRotation.Angle = 0;
            _physicsTimer.Stop();
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        HoverActions.IsHitTestVisible = true;
        HoverActions.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120)));
        CardFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0xC8, 0xFA));

        var scaleAnim = new DoubleAnimation(1.035, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            HoverActions.IsHitTestVisible = false;
            HoverActions.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140)));
            CardFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40));

            var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject dep && IsInsideButton(dep))
            return;

        if (Parent is not Canvas canvas) return;

        _isPointerDown = true;
        _isDragging = false;
        CaptureMouse();

        var pos = e.GetPosition(canvas);
        _pressStartOnCanvas = pos;
        _initialCardLeft = Canvas.GetLeft(this);
        if (double.IsNaN(_initialCardLeft)) _initialCardLeft = 0;
        _initialCardTop = Canvas.GetTop(this);
        if (double.IsNaN(_initialCardTop)) _initialCardTop = 0;
        _grabOffsetX = pos.X - _initialCardLeft;
        _lastLeft = _initialCardLeft;

        CardTranslation.X = 0;
        CardTranslation.Y = 0;
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDown || Parent is not Canvas canvas) return;

        var pos = e.GetPosition(canvas);
        if (!_isDragging)
        {
            double dx = pos.X - _pressStartOnCanvas.X;
            double dy = pos.Y - _pressStartOnCanvas.Y;
            if ((dx * dx + dy * dy) < (DragThreshold * DragThreshold))
                return;

            _isDragging = true;
            DragStateChanged?.Invoke(this, true);
            CardRotation.Angle = 0;
        }

        double targetLeft = pos.X - _grabOffsetX;
        CardTranslation.X = targetLeft - _initialCardLeft;
        _lastLeft = targetLeft;

        PositionChanged?.Invoke(this, targetLeft + (ActualWidth / 2.0));
    }

    private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_isPointerDown) return;

        _isPointerDown = false;
        ReleaseMouseCapture();

        if (_isDragging)
        {
            _isDragging = false;
            DragStateChanged?.Invoke(this, false);

            double finalLeft = _initialCardLeft + CardTranslation.X;
            CardTranslation.X = 0;
            CardTranslation.Y = 0;
            Canvas.SetLeft(this, finalLeft);
            PositionChanged?.Invoke(this, finalLeft + (ActualWidth / 2.0));
            ApplyImpulse(6.0);
        }
        else
        {
            CardTranslation.X = 0;
            CardTranslation.Y = 0;
            // Single click anywhere on the card copies text to clipboard
            CopyToClipboard();
        }
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _physicsTimer.Stop();
        CardRemoved?.Invoke(this, EventArgs.Empty);
    }

    private void CopyToClipboard()
    {
        CardCopied?.Invoke(this, ClipText);
        HintText.Text = "✓ Copied!";
        HintText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));

        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        resetTimer.Tick += (s, e) =>
        {
            resetTimer.Stop();
            HintText.Text = "Click card to copy";
            HintText.Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x66));
        };
        resetTimer.Start();
    }

    private static bool IsInsideButton(DependencyObject? dep)
    {
        while (dep != null)
        {
            if (dep is System.Windows.Controls.Button) return true;
            if (dep is System.Windows.Media.Visual || dep is System.Windows.Media.Media3D.Visual3D)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            else
            {
                dep = LogicalTreeHelper.GetParent(dep);
            }
        }
        return false;
    }
}
