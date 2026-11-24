namespace FactorySim.Core;

/// <summary>
/// An axis that moves only while a direction is commanded and stays where it is otherwise,
/// like a lifting table on an up and a down contactor. Position runs from 0 (bottom) to the
/// travel (top). Commanding both directions at once does nothing.
/// </summary>
public sealed class JogAxis
{
    private readonly double _travel;
    private readonly double _speed;

    public JogAxis(double travel, double speed)
    {
        if (!(travel > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(travel), "Travel must be positive.");
        }

        if (!(speed > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be positive.");
        }

        _travel = travel;
        _speed = speed;
    }

    public double Travel => _travel;

    public bool Up { get; set; }

    public bool Down { get; set; }

    /// <summary>Distance from the bottom, in metres.</summary>
    public double Position { get; private set; }

    /// <summary>True when the last tick moved the axis.</summary>
    public bool Moving { get; private set; }

    public bool AtMin => Position <= PusherLogic.LimitTolerance;

    public bool AtMax => Position >= _travel - PusherLogic.LimitTolerance;

    public void Tick(double deltaSeconds)
    {
        double direction = Up == Down ? 0.0 : Up ? 1.0 : -1.0;
        double next = Math.Clamp(Position + direction * _speed * deltaSeconds, 0.0, _travel);
        Moving = Math.Abs(next - Position) > 1e-12;
        Position = next;
    }

    public void Reset()
    {
        Up = false;
        Down = false;
        Position = 0.0;
        Moving = false;
    }
}
