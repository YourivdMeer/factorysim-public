using System.Globalization;

namespace FactorySim.Core.Robot;

/// <summary>
/// The angles of the six axes of a robot arm, in degrees. All zero is home: the upper arm
/// upright, the forearm level and forward, and the tool pointing straight down.
/// </summary>
public readonly record struct RobotAngles(double A1, double A2, double A3, double A4, double A5, double A6)
{
    public const int Count = 6;

    public static readonly RobotAngles Home = new(0, 0, 0, 0, 0, 0);

    public double this[int axis] => axis switch
    {
        0 => A1,
        1 => A2,
        2 => A3,
        3 => A4,
        4 => A5,
        5 => A6,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    public static RobotAngles FromArray(IReadOnlyList<double> values)
    {
        if (values.Count != Count)
        {
            throw new ArgumentException($"A robot has {Count} angles, not {values.Count}.", nameof(values));
        }

        return new RobotAngles(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public double[] ToArray() => new[] { A1, A2, A3, A4, A5, A6 };

    /// <summary>The largest difference of one axis between this and <paramref name="other"/>, in degrees.</summary>
    public double MaxDifference(RobotAngles other)
    {
        double max = 0.0;
        for (int axis = 0; axis < Count; axis++)
        {
            max = Math.Max(max, Math.Abs(this[axis] - other[axis]));
        }

        return max;
    }

    public override string ToString() => string.Join(", ",
        ToArray().Select(value => value.ToString("0.##", CultureInfo.InvariantCulture)));
}
