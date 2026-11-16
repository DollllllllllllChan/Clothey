using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace PegSnap.UI.Tray;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _hotkeyMenuItem;
    private readonly ToolStripMenuItem _autoHideMenuItem;
    private readonly ToolStripMenuItem _autoPinTextMenuItem;
    private bool _disposed;

    public event EventHandler? SnipRequested;
    public event EventHandler? RadialMenuRequested;
    public event EventHandler? ClearClotheslineRequested;
    public event EventHandler<bool>? HotkeyToggleRequested;
    public event EventHandler<bool>? AutoHideToggleRequested;
    public event EventHandler<bool>? AutoPinTextToggleRequested;
    public event EventHandler? ExitRequested;

    public TrayIconManager()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Clothey - F1 Tap to Snip, Hold for Radial Menu",
            Visible = true,
            Icon = CreateAppIcon()
        };

        var contextMenu = new ContextMenuStrip();

        var headerItem = new ToolStripMenuItem("⚡ Clothey v1.0") { Enabled = false };
        headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);

        var snipItem = new ToolStripMenuItem("📸 Snip Area (F1 Tap)", null, (s, e) => SnipRequested?.Invoke(this, EventArgs.Empty));
        var radialItem = new ToolStripMenuItem("🎯 Radial Menu (F1 Hold)", null, (s, e) => RadialMenuRequested?.Invoke(this, EventArgs.Empty));
        var clearItem = new ToolStripMenuItem("🧺 Clear Clothesline", null, (s, e) => ClearClotheslineRequested?.Invoke(this, EventArgs.Empty));

        _hotkeyMenuItem = new ToolStripMenuItem("⚡ F1 Hotkey Active");
        _hotkeyMenuItem.Checked = true;
        _hotkeyMenuItem.Click += (s, e) =>
        {
            if (s is ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                HotkeyToggleRequested?.Invoke(this, item.Checked);
            }
        };

        _autoHideMenuItem = new ToolStripMenuItem("🫥 Auto-Hide Top Edge");
        _autoHideMenuItem.Checked = true;
        _autoHideMenuItem.Click += (s, e) =>
        {
            if (s is ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                AutoHideToggleRequested?.Invoke(this, item.Checked);
            }
        };

        _autoPinTextMenuItem = new ToolStripMenuItem("📋 Auto-Pin Copy Text");
        _autoPinTextMenuItem.Checked = true;
        _autoPinTextMenuItem.Click += (s, e) =>
        {
            if (s is ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                AutoPinTextToggleRequested?.Invoke(this, item.Checked);
            }
        };

        var openFolderItem = new ToolStripMenuItem("📁 Open Temp Folder", null, (s, e) =>
        {
            string path = Path.Combine(Path.GetTempPath(), "Clothey", "Captures");
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
        });

        var aboutItem = new ToolStripMenuItem("ℹ️ About Clothey", null, (s, e) =>
        {
            System.Windows.MessageBox.Show(
                "Clothey v1.0\nUltra-lightweight Windows Native Screen Capture & Clothesline Utility.\n\n" +
                "• F1 Tap (<300ms): Snipping Area\n" +
                "• F1 Hold (>=300ms): Radial Pie Menu\n" +
                "• Top Clothesline: Drag-and-drop to Discord/WhatsApp/Explorer!\n\n" +
                "Built with C# .NET 8 WPF.",
                "About Clothey",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        });

        var exitItem = new ToolStripMenuItem("❌ Exit Clothey", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

        contextMenu.Items.Add(headerItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(snipItem);
        contextMenu.Items.Add(radialItem);
        contextMenu.Items.Add(clearItem);
        contextMenu.Items.Add(_hotkeyMenuItem);
        contextMenu.Items.Add(_autoHideMenuItem);
        contextMenu.Items.Add(_autoPinTextMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(openFolderItem);
        contextMenu.Items.Add(aboutItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => SnipRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowNotification(string title, string text)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    private static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Background cyan circle
            using (var brush = new SolidBrush(Color.FromArgb(14, 165, 233)))
            {
                g.FillEllipse(brush, 2, 2, 28, 28);
            }

            // Dark camera body
            using (var brush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            {
                g.FillRectangle(brush, 7, 11, 18, 13);
                g.FillRectangle(brush, 11, 8, 10, 4);
            }

            // Cyan lens
            using (var brush = new SolidBrush(Color.FromArgb(56, 182, 255)))
            {
                g.FillEllipse(brush, 11, 13, 10, 10);
            }

            // White peg pin highlight
            using (var brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, 13, 15, 4, 4);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
