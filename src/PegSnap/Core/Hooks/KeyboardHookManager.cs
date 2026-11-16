using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PegSnap.Core.Win32;

namespace PegSnap.Core.Hooks;

public class KeyboardHookManager : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private bool _isF1Down;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public event EventHandler? AreaSnipRequested;

    public KeyboardHookManager()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hModule = IntPtr.Zero;
        if (curModule != null)
        {
            hModule = NativeMethods.GetModuleHandle(curModule.ModuleName);
        }

        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hModule, 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _isF1Down = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int message = wParam.ToInt32();
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            if (kb.vkCode == NativeMethods.VK_F1)
            {
                if (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN)
                {
                    if (!_isF1Down)
                    {
                        _isF1Down = true;
                        // Instant 0ms trigger - ultra fast, zero lag!
                        AreaSnipRequested?.Invoke(this, EventArgs.Empty);
                    }
                    return (IntPtr)1;
                }
                else if (message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP)
                {
                    _isF1Down = false;
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    ~KeyboardHookManager()
    {
        Dispose();
    }
}
