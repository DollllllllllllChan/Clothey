using System;
using PegSnap.Core.Physics;
using Xunit;

namespace PegSnap.Tests;

public class DampedHarmonicOscillatorTests
{
    [Fact]
    public void CalculateAngle_AtTimeZero_ReturnsInitialAngle()
    {
        var oscillator = new DampedHarmonicOscillator(initialAngleDegrees: 18.0);
        double angle = oscillator.CalculateAngle(0);

        Assert.Equal(18.0, angle, precision: 3);
    }

    [Fact]
    public void CalculateAngle_OverTime_DecaysBelowInitialAmplitude()
    {
        var oscillator = new DampedHarmonicOscillator(initialAngleDegrees: 20.0, dampingRatio: 0.2, naturalFrequency: 14.0);

        double angleAfterOneSec = oscillator.CalculateAngle(1.0);
        Assert.True(Math.Abs(angleAfterOneSec) < 20.0);
    }

    [Fact]
    public void IsSettled_ReturnsFalseInitially_AndTrueAfterDamping()
    {
        var oscillator = new DampedHarmonicOscillator(initialAngleDegrees: 18.0, dampingRatio: 0.22, naturalFrequency: 14.0);

        // At t = 0, amplitude is 18.0, not settled
        Assert.False(oscillator.IsSettled(0.0, thresholdDegrees: 0.1));

        // At t = 2.0s, envelope = 18 * exp(-0.22 * 14 * 2.0) = 18 * exp(-6.16) ≈ 0.038 <= 0.1
        Assert.True(oscillator.IsSettled(2.0, thresholdDegrees: 0.1));
    }

    [Fact]
    public void Constructor_ClampsDampingRatio()
    {
        var oscillatorNegative = new DampedHarmonicOscillator(18.0, -0.5, 10.0);
        Assert.True(oscillatorNegative.DampingRatio >= 0.01);

        var oscillatorOverdamped = new DampedHarmonicOscillator(18.0, 1.5, 10.0);
        Assert.True(oscillatorOverdamped.DampingRatio <= 0.99);
    }
}
