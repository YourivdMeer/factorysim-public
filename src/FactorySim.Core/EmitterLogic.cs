namespace FactorySim.Core;

/// <summary>
/// Decides when the emitter spawns a box and of which kind.
/// Deterministic for a given seed, so a scene run can be reproduced.
/// </summary>
public sealed class EmitterLogic
{
    private const double Epsilon = 1e-9;

    private readonly double _intervalSeconds;
    private readonly int _seed;
    private readonly double _highRatio;
    private Random _random;
    private double _timeUntilNext;

    /// <param name="intervalSeconds">Minimum time between two boxes.</param>
    /// <param name="seed">Seed for the low/high sequence.</param>
    /// <param name="highRatio">Share of high boxes, 0..1.</param>
    public EmitterLogic(double intervalSeconds, int seed, double highRatio = 0.5)
    {
        if (!(intervalSeconds > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be positive.");
        }

        if (!(highRatio >= 0.0 && highRatio <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(highRatio), "Ratio must be between 0 and 1.");
        }

        _intervalSeconds = intervalSeconds;
        _seed = seed;
        _highRatio = highRatio;
        _random = new Random(seed);
    }

    /// <summary>Emitter on/off; this is what the actuator tag will drive.</summary>
    public bool Enabled { get; set; }

    public int EmittedCount { get; private set; }

    /// <summary>
    /// Advances the emitter by one physics step.
    /// </summary>
    /// <param name="deltaSeconds">Step length.</param>
    /// <param name="volumeOccupied">True while a box is still inside the spawn volume.</param>
    /// <returns>The kind of box to spawn this step, or null.</returns>
    public BoxKind? Tick(double deltaSeconds, bool volumeOccupied)
    {
        if (!Enabled)
        {
            return null;
        }

        // Waiting time does not accumulate: a blocked emitter emits one box
        // when it clears, not a burst.
        _timeUntilNext = Math.Max(0.0, _timeUntilNext - deltaSeconds);

        if (_timeUntilNext > Epsilon || volumeOccupied)
        {
            return null;
        }

        _timeUntilNext = _intervalSeconds;
        EmittedCount++;
        return _random.NextDouble() < _highRatio ? BoxKind.High : BoxKind.Low;
    }

    /// <summary>Restarts the sequence from the seed. Keeps <see cref="Enabled"/>.</summary>
    public void Reset()
    {
        _random = new Random(_seed);
        _timeUntilNext = 0.0;
        EmittedCount = 0;
    }
}
