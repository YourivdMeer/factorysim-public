namespace FactorySim.Core;

/// <summary>
/// An incremental encoder with two tracks. It gives one period of A and B for every pulse distance
/// that the surface under its wheel moves. The tracks are a quarter period apart, so that the PLC
/// can tell the direction: moving forward B follows A, backwards A follows B.
/// <code>
/// phase    0 ..0.25 ..0.5 ..0.75 ..1
/// A        1    1     0     0      1
/// B        0    1     1     0      0
/// </code>
/// </summary>
public sealed class EncoderLogic
{
    private readonly double _pulseDistance;

    /// <param name="pulseDistance">Distance in metres that makes one full period of A and B.</param>
    public EncoderLogic(double pulseDistance)
    {
        if (!(pulseDistance > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(pulseDistance), "The pulse distance must be positive.");
        }

        _pulseDistance = pulseDistance;
    }

    /// <summary>Distance travelled since the start, in metres; negative after going backwards.</summary>
    public double Distance { get; private set; }

    public bool A => Fraction(Distance / _pulseDistance) < 0.5;

    public bool B => Fraction(Distance / _pulseDistance - 0.25) < 0.5;

    /// <param name="distance">Distance the surface moved under the wheel, in metres; negative when it moves back.</param>
    public void Advance(double distance) => Distance += distance;

    public void Reset() => Distance = 0.0;

    private static double Fraction(double value) => value - Math.Floor(value);
}
