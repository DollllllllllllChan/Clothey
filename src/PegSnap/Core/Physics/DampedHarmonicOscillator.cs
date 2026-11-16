using System;

namespace PegSnap.Core.Physics;

/// <summary>
/// Calculates damped harmonic pendulum physics for clothesline cards:
/// θ(t) = θ₀ * e^(-ζ * ω_n * t) * cos(ω_d * t)
/// where ω_d = ω_n * sqrt(1 - ζ²)
/// </summary>
public class DampedHarmonicOscillator
{
    public double InitialAngleDegrees { get; set; }
    public double DampingRatio { get; set; } // ζ (zeta), between 0 and 1 (underdamped)
    public double NaturalFrequency { get; set; } // ω_n (omega_n, rad/s)

    public DampedHarmonicOscillator(double initialAngleDegrees = 18.0, double dampingRatio = 0.22, double naturalFrequency = 14.0)
    {
        InitialAngleDegrees = initialAngleDegrees;
        DampingRatio = Math.Clamp(dampingRatio, 0.01, 0.99);
        NaturalFrequency = Math.Max(0.1, naturalFrequency);
    }

    /// <summary>
    /// Calculates current angle in degrees at time t (seconds).
    /// </summary>
    public double CalculateAngle(double timeSeconds)
    {
        if (timeSeconds <= 0) return InitialAngleDegrees;

        double omegaD = NaturalFrequency * Math.Sqrt(1.0 - (DampingRatio * DampingRatio));
        double decay = Math.Exp(-DampingRatio * NaturalFrequency * timeSeconds);
        return InitialAngleDegrees * decay * Math.Cos(omegaD * timeSeconds);
    }

    /// <summary>
    /// Calculates the envelope magnitude at time t (seconds).
    /// </summary>
    public double CalculateEnvelope(double timeSeconds)
    {
        if (timeSeconds <= 0) return Math.Abs(InitialAngleDegrees);
        return Math.Abs(InitialAngleDegrees) * Math.Exp(-DampingRatio * NaturalFrequency * timeSeconds);
    }

    /// <summary>
    /// Returns true when the oscillation has decayed below threshold, indicating we can stop animating to save CPU.
    /// </summary>
    public bool IsSettled(double timeSeconds, double thresholdDegrees = 0.1)
    {
        return CalculateEnvelope(timeSeconds) <= thresholdDegrees;
    }
}
