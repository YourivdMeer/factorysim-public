namespace FactorySim.Core.Robot;

/// <summary>
/// Point-to-point motion in joint space: every axis turns straight from its start angle to its
/// end angle at a constant speed, and all axes arrive together. The slowest axis, at its top
/// speed times <see cref="SpeedPercent"/>, sets the duration.
/// </summary>
public sealed class RobotMotion
{
    public const double MinSpeedPercent = 5.0;

    public const double MaxSpeedPercent = 100.0;

    private readonly RobotGeometry _geometry;
    private RobotAngles _from = RobotAngles.Home;
    private RobotAngles _to = RobotAngles.Home;
    private double _duration;
    private double _elapsed;

    public RobotMotion(RobotGeometry geometry, double speedPercent)
    {
        _geometry = geometry;
        SpeedPercent = Math.Clamp(speedPercent, MinSpeedPercent, MaxSpeedPercent);
    }

    /// <summary>Speed as a percentage of the top speed of the axes, 5 to 100.</summary>
    public double SpeedPercent { get; }

    public RobotAngles Angles { get; private set; } = RobotAngles.Home;

    public bool Busy { get; private set; }

    /// <summary>Starts a motion from where the arm is now; a target outside the axis ranges is clamped to them.</summary>
    public void MoveTo(RobotAngles target)
    {
        _from = Angles;
        _to = _geometry.Clamp(target);
        _duration = 0.0;
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            double speed = _geometry.MaxSpeed[axis] * SpeedPercent / 100.0;
            _duration = Math.Max(_duration, Math.Abs(_to[axis] - _from[axis]) / speed);
        }

        _elapsed = 0.0;
        Busy = _duration > 0.0;
        if (!Busy)
        {
            Angles = _to;
        }
    }

    /// <summary>Puts the arm on <paramref name="angles"/> at once, ending any motion; for the editor and reset.</summary>
    public void Jump(RobotAngles angles)
    {
        Angles = _geometry.Clamp(angles);
        _from = Angles;
        _to = Angles;
        Busy = false;
    }

    public void Tick(double deltaSeconds)
    {
        if (!Busy)
        {
            return;
        }

        _elapsed += deltaSeconds;
        if (_elapsed >= _duration)
        {
            Angles = _to;
            Busy = false;
            return;
        }

        double s = _elapsed / _duration;
        var values = new double[RobotAngles.Count];
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            values[axis] = _from[axis] + (_to[axis] - _from[axis]) * s;
        }

        Angles = RobotAngles.FromArray(values);
    }
}
