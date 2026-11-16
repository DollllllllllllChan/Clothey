using System;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using PegSnap.Core.Recording;

namespace PegSnap.UI.Recording;

public partial class RecordingHudWindow : Window
{
    private readonly MiniRecorderManager _recorder = new();
    public event EventHandler<BitmapSource>? RecordingFinished;

    public RecordingHudWindow()
    {
        InitializeComponent();

        _recorder.Tick += OnRecorderTick;
        _recorder.RecordingCompleted += OnRecorderCompleted;

        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Enter)
            {
                Stop();
            }
        };

        MouseDown += (s, e) =>
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    public void StartRecording(Rectangle? bounds = null)
    {
        // Position at bottom center of screen
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
        Top = SystemParameters.PrimaryScreenHeight - 100;

        TimerText.Text = "00:00";
        Show();
        Activate();

        // Pulsing red dot animation
        var pulse = new DoubleAnimation(1.0, 0.2, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        RecDot.BeginAnimation(OpacityProperty, pulse);

        _recorder.Start(bounds);
    }

    private void OnRecorderTick(object? sender, TimeSpan elapsed)
    {
        TimerText.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void OnRecorderCompleted(object? sender, BitmapSource frame)
    {
        Hide();
        RecordingFinished?.Invoke(this, frame);
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        Stop();
    }

    public void Stop()
    {
        _recorder.Stop();
    }
}
