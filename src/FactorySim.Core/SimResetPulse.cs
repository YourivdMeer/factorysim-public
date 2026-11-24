namespace FactorySim.Core;

/// <summary>
/// The pulse of the tag <c>Sim reset</c> (plan 14): high for <see cref="Duration"/> after each reset.
/// Fed with real time in seconds, because after a reset the simulation stands still and simulation
/// time does not run. A reset during the pulse starts the full second again.
/// </summary>
public sealed class SimResetPulse
{
    /// <summary>How long the tag stays high, in seconds of real time.</summary>
    public const double Duration = 1.0;

    private double? _until;

    /// <summary>A reset happened at <paramref name="now"/> (seconds of real time).</summary>
    public void Trigger(double now) => _until = now + Duration;

    /// <summary>True while the pulse lasts at <paramref name="now"/>.</summary>
    public bool IsHigh(double now) => _until is { } until && now < until;
}
