using PegSnap.Core.Hooks;
using Xunit;

namespace PegSnap.Tests;

public class HotkeyTimingTests
{
    [Theory]
    [InlineData(0, HotkeyActionType.Tap)]
    [InlineData(150, HotkeyActionType.Tap)]
    [InlineData(299, HotkeyActionType.Tap)]
    [InlineData(300, HotkeyActionType.Hold)]
    [InlineData(500, HotkeyActionType.Hold)]
    [InlineData(1200, HotkeyActionType.Hold)]
    public void Evaluate_ReturnsExpectedActionType(long elapsedMs, HotkeyActionType expected)
    {
        var action = HotkeyTimingEvaluator.Evaluate(elapsedMs, thresholdMs: 300);
        Assert.Equal(expected, action);
    }

    [Fact]
    public void Evaluate_NegativeDuration_ReturnsNone()
    {
        var action = HotkeyTimingEvaluator.Evaluate(-10);
        Assert.Equal(HotkeyActionType.None, action);
    }
}
