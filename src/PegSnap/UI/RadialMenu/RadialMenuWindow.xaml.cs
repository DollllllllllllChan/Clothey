using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PegSnap.Core.Radial;

namespace PegSnap.UI.RadialMenu;

public partial class RadialMenuWindow : Window
{
    private readonly RadialMenuEngine _engine = new() { DeadZoneRadius = 38.0 };
    private Point _centerPoint;
    public RadialSectorType CurrentSector { get; private set; } = RadialSectorType.None;

    public event EventHandler<RadialSectorType>? SectorExecuted;

    public RadialMenuWindow()
    {
        InitializeComponent();
        MouseMove += OnWindowMouseMove;
        KeyDown += OnWindowKeyDown;
        MouseRightButtonDown += (s, e) => Hide();
    }

    public void ShowAt(Point screenPoint)
    {
        // Center the 380x380 window on cursor
        Left = screenPoint.X - (Width / 2.0);
        Top = screenPoint.Y - (Height / 2.0);
        _centerPoint = new Point(Width / 2.0, Height / 2.0);

        CurrentSector = RadialSectorType.None;
        UpdateHighlights();
        Show();
        Activate();
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        double deltaX = pos.X - _centerPoint.X;
        double deltaY = pos.Y - _centerPoint.Y;

        var sector = _engine.GetSector(deltaX, deltaY);
        if (sector != CurrentSector)
        {
            CurrentSector = sector;
            UpdateHighlights();
        }
    }

    private void UpdateHighlights()
    {
        ResetHighlight(SectorTopBorder, SectorTopShadow, "#EB161926", "#33FFFFFF", Colors.Black, 0.4);
        ResetHighlight(SectorRightBorder, SectorRightShadow, "#EB161926", "#33FFFFFF", Colors.Black, 0.4);
        ResetHighlight(SectorBottomBorder, SectorBottomShadow, "#EB161926", "#33FFFFFF", Colors.Black, 0.4);
        ResetHighlight(SectorLeftBorder, SectorLeftShadow, "#EB161926", "#33FFFFFF", Colors.Black, 0.4);

        if (CurrentSector == RadialSectorType.None)
        {
            CenterIcon.Text = "✕";
            CenterIcon.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            CenterHubBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            CenterHubShadow.Color = Color.FromRgb(239, 68, 68);
            CenterHubShadow.Opacity = 0.5;
            StatusLabel.Text = "Batal";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
        else
        {
            var (title, _, _) = RadialMenuEngine.GetSectorInfo(CurrentSector);
            Color accentColor = CurrentSector switch
            {
                RadialSectorType.AreaCapture => Color.FromRgb(56, 182, 255),
                RadialSectorType.ScreenRecording => Color.FromRgb(239, 68, 68),
                RadialSectorType.TextOcr => Color.FromRgb(16, 185, 129),
                RadialSectorType.FullCapture => Color.FromRgb(139, 92, 246),
                _ => Color.FromRgb(96, 165, 250)
            };

            CenterIcon.Text = "⚡";
            CenterIcon.Foreground = new SolidColorBrush(accentColor);
            CenterHubBorder.BorderBrush = new SolidColorBrush(accentColor);
            CenterHubShadow.Color = accentColor;
            CenterHubShadow.Opacity = 0.7;
            StatusLabel.Text = "Lepas";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        switch (CurrentSector)
        {
            case RadialSectorType.AreaCapture:
                SetHighlight(SectorTopBorder, SectorTopShadow, "#F21D2C42", "#38B6FF", Color.FromRgb(56, 182, 255), 1.07, 0.75);
                break;
            case RadialSectorType.ScreenRecording:
                SetHighlight(SectorRightBorder, SectorRightShadow, "#F2351E28", "#EF4444", Color.FromRgb(239, 68, 68), 1.07, 0.75);
                break;
            case RadialSectorType.TextOcr:
                SetHighlight(SectorBottomBorder, SectorBottomShadow, "#F218362D", "#10B981", Color.FromRgb(16, 185, 129), 1.07, 0.75);
                break;
            case RadialSectorType.FullCapture:
                SetHighlight(SectorLeftBorder, SectorLeftShadow, "#F22A1E42", "#8B5CF6", Color.FromRgb(139, 92, 246), 1.07, 0.75);
                break;
        }
    }

    private static void ResetHighlight(Border border, DropShadowEffect shadow, string bgHex, string borderHex, Color shadowColor, double shadowOpacity)
    {
        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
        border.BorderThickness = new Thickness(1.2);
        border.RenderTransform = null;
        shadow.Color = shadowColor;
        shadow.Opacity = shadowOpacity;
    }

    private static void SetHighlight(Border border, DropShadowEffect shadow, string bgHex, string borderHex, Color shadowColor, double scale, double shadowOpacity)
    {
        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
        border.BorderThickness = new Thickness(2.0);
        border.RenderTransform = new ScaleTransform(scale, scale, border.Width / 2.0, border.Height / 2.0);
        shadow.Color = shadowColor;
        shadow.Opacity = shadowOpacity;
    }

    /// <summary>
    /// Called when F1 key is released after being held >= 300ms.
    /// If in center deadzone (None), cleanly cancels without executing any action.
    /// </summary>
    public void ExecuteCurrentAndClose()
    {
        var sector = CurrentSector;
        Hide();

        if (sector == RadialSectorType.None)
        {
            // Cancelled cleanly!
            return;
        }

        SectorExecuted?.Invoke(this, sector);
    }

    private void OnCenterHubMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Explicitly clicking the center hub cancels
        Hide();
    }

    private void OnSectorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tagStr)
        {
            if (Enum.TryParse<RadialSectorType>(tagStr, out var sector))
            {
                CurrentSector = sector;
                ExecuteCurrentAndClose();
            }
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }
}
