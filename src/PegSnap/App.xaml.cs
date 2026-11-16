using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PegSnap.Core.Capture;
using PegSnap.Core.Hooks;
using PegSnap.Core.Ocr;
using PegSnap.Core.Win32;
using PegSnap.UI.Clothesline;
using PegSnap.UI.Preview;
using PegSnap.UI.Snipping;
using PegSnap.UI.Tray;

namespace PegSnap;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _wakeUpEvent;
    private static RegisteredWaitHandle? _registeredWaitHandle;
    private KeyboardHookManager? _keyboardHook;
    private TrayIconManager? _trayManager;
    private ClotheslineWindow? _clotheslineWindow;
    private SnippingWindow? _snippingWindow;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            Debug.WriteLine($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        const string mutexName = "Local\\Clothey_SingleInstance_App_Mutex";
        const string wakeUpEventName = "Local\\Clothey_SingleInstance_WakeUp_Event";

        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            // Another instance is already running! Wake it up and trigger instant snipping
            try
            {
                using var wakeEvent = EventWaitHandle.OpenExisting(wakeUpEventName);
                wakeEvent.Set();
            }
            catch { }

            Environment.Exit(0);
            return;
        }

        // Register wake-up handler for future launches from ScApp or desktop shortcuts
        try
        {
            _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, wakeUpEventName);
            _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(_wakeUpEvent, (state, timedOut) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _clotheslineWindow?.EnforceTopmost(force: true);
                    _clotheslineWindow?.TriggerRopeBounce(12.0);
                    _clotheslineWindow?.ShowToast("⚡", "Clothey Active • Drag to Snip");
                    TriggerAreaSnip();
                });
            }, null, -1, false);
        }
        catch { }

        // 0. Set high-resolution PerMonitorV2 context for 100% crisp, native physical pixel capture
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }

        // 1. Initialize Windows (Fast & lightweight, no radial menu overhead)
        _clotheslineWindow = new ClotheslineWindow();
        _snippingWindow = new SnippingWindow();

        // 2. Setup Tray Manager
        _trayManager = new TrayIconManager();
        _trayManager.SnipRequested += (s, ev) => TriggerAreaSnip();
        _trayManager.ClearClotheslineRequested += (s, ev) => _clotheslineWindow.ClearAll();
        _trayManager.HotkeyToggleRequested += (s, enabled) =>
        {
            if (_keyboardHook != null) _keyboardHook.IsEnabled = enabled;
        };
        _trayManager.AutoHideToggleRequested += (s, enabled) => _clotheslineWindow.SetAutoHideEnabled(enabled);
        _trayManager.AutoPinTextToggleRequested += (s, enabled) => _clotheslineWindow.SetAutoPinTextEnabled(enabled);
        _trayManager.ExitRequested += (s, ev) => Shutdown();

        // 3. Setup Keyboard Hook (Instant F1 = 0ms Snipping)
        _keyboardHook = new KeyboardHookManager();
        _keyboardHook.AreaSnipRequested += (s, ev) => Dispatcher.Invoke(TriggerAreaSnip);
        _keyboardHook.Start();

        // 4. Wire Snipping Window Capture Results (Zero-Copy Native Disk Cache + 40KB Thumbnails)
        _snippingWindow.AreaCaptured += (s, data) =>
        {
            NativeMethods.GetCursorPos(out var pt);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            _clotheslineWindow.AddCapturedScreenshot(data.filePath, data.thumbnail, data.origW, data.origH, screen);
            CaptureHelper.CopyToClipboard(data.filePath);
            _clotheslineWindow.ShowToast("✂", "Area Captured!");

            ScheduleWorkingSetTrim(300);
        };

        _snippingWindow.OcrCaptured += async (s, data) =>
        {
            NativeMethods.GetCursorPos(out var pt);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            _clotheslineWindow.AddCapturedScreenshot(data.filePath, data.thumbnail, data.origW, data.origH, screen);
            _clotheslineWindow.ShowToast("⏳", "Extracting OCR text...");

            string text = await OcrService.ExtractTextFromFileAsync(data.filePath);
            Clipboard.SetText(text);

            int wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            _clotheslineWindow.ShowToast("📄", $"OCR: {wordCount} words copied!");

            ScheduleWorkingSetTrim(300);
        };

        // 5. Show Clothesline on screen top with welcoming bounce & dynamic island banner
        _clotheslineWindow.Show();
        _clotheslineWindow.TriggerRopeBounce(14.0);
        _clotheslineWindow.ShowToast("🧺", "Clothey Ready • Press F1 to Snip!");

        // 6. Optimize Working Set & keep RAM locked at ultra-low ~10 MB
        ScheduleWorkingSetTrim(800);

        var idleTrimTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        idleTrimTimer.Tick += (s, e) => NativeMethods.TrimWorkingSet();
        idleTrimTimer.Start();
    }

    public static void ScheduleWorkingSetTrim(int delayMs = 1200)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            NativeMethods.TrimWorkingSet();
        };
        timer.Start();
    }

    private void TriggerAreaSnip()
    {
        _snippingWindow?.StartSnip(SnipMode.StandardArea);
    }

    private void TriggerOcrSnip()
    {
        _snippingWindow?.StartSnip(SnipMode.OcrArea);
    }

    private void TriggerFullWindowCapture()
    {
        try
        {
            NativeMethods.GetCursorPos(out var pt);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));

            using var bmp = CaptureHelper.CaptureForegroundWindow() ?? CaptureHelper.CaptureVirtualScreen();
            var data = CaptureHelper.ProcessCapturedBitmap(bmp);
            _clotheslineWindow?.AddCapturedScreenshot(data.filePath, data.thumbnail, data.origW, data.origH, screen);
            CaptureHelper.CopyToClipboard(data.filePath);
            _clotheslineWindow?.ShowToast("🖵", "Full Window Captured!");
            ScheduleWorkingSetTrim(300);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Full window capture failed: {ex.Message}");
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _registeredWaitHandle?.Unregister(null);
        _wakeUpEvent?.Dispose();
        _wakeUpEvent = null;

        // Clean unhook on exit to prevent any input lag on Windows OS
        _keyboardHook?.Dispose();
        _trayManager?.Dispose();

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}
