using System;

namespace PegSnap.Core.Hooks;

public enum HotkeyActionType
{
    None = 0,
    Tap = 1,  // < 300ms: Snipping Area Capture
    Hold = 2  // >= 300ms: Radial Menu
}

public static class HotkeyTimingEvaluator
{
    public const int DefaultHoldThresholdMs = 300;

    /// <summary>
    /// Evaluates whether an elapsed press duration corresponds to a Tap or a Hold.
    /// </summary>
    public static HotkeyActionType Evaluate(long elapsedMilliseconds, int thresholdMs = DefaultHoldThresholdMs)
    {
        if (elapsedMilliseconds < 0) return HotkeyActionType.None;
        if (elapsedMilliseconds < thresholdMs) return HotkeyActionType.Tap;
        return HotkeyActionType.Hold;
    }
}
