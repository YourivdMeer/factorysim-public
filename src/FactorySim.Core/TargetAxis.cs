namespace FactorySim.Core;

/// <summary>
/// An axis that runs to a commanded position at constant speed, or moves while a direction is
/// commanded (jog). Used for the drive and the hoist of a stacker crane. A blocked axis keeps
/// its target and carries on when it is released.
/// </summary>
public sealed class TargetAxis
{
    private readonly double _min;
    private readonly double _max;
    private readonly double _speed;
    private readonly double _start;
    private double? _target;

    public TargetAxis(double min, double max, double speed, double start)
    {
        if (!(max > min))
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum must be above minimum.");
        }

        if (!(speed > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be positive.");
        }

        _min = min;
        _max = max;
        _speed = speed;
        _start = Math.Clamp(start, min, max);
        Position = _start;
    }

    public double Position { get; private set; }

    /// <summary>True when the last tick moved the axis.</summary>
    public bool Moving { get; private set; }

    /// <summary>The axis has a target it has not reached yet.</summary>
    public bool Pending => _target.HasValue;

    /// <summary>Jog command: 1 up, -1 down, 0 none. Ignored while a target is pending.</summary>
    public int Jog { get; set; }

    /// <summary>Runs to a position (limited to the travel).</summary>
    public void MoveTo(double target) => _target = Math.Clamp(target, _min, _max);

    /// <param name="deltaSeconds">Length of the step.</param>
    /// <param name="enabled">False holds the axis where it is.</param>
    public void Tick(double deltaSeconds, bool enabled = true)
    {
        double before = Position;
        if (enabled)
        {
            double maxStep = _speed * deltaSeconds;
            if (_target.HasValue)
            {
                double delta = _target.Value - Position;
                if (Math.Abs(delta) <= maxStep)
                {
                    Position = _target.Value;
                    _target = null;
                }
                else
                {
                    Position += Math.Sign(delta) * maxStep;
                }
            }
            else if (Jog != 0)
            {
                Position = Math.Clamp(Position + Math.Sign(Jog) * maxStep, _min, _max);
            }
        }

        Moving = Math.Abs(Position - before) > 1e-12;
    }

    public void Reset()
    {
        Position = _start;
        _target = null;
        Jog = 0;
        Moving = false;
    }
}
