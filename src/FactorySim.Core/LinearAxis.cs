namespace FactorySim.Core;

/// <summary>
/// One monostable axis of a station: travels to the end of its stroke while commanded and
/// back otherwise, at constant speed. An obstacle can stop it short on the way out, which
/// is how a gripper comes to rest on top of an item of any height.
/// </summary>
public sealed class LinearAxis
{
    private readonly double _stroke;
    private readonly double _speed;

    /// <param name="stroke">Travel from start to end, in metres.</param>
    /// <param name="speed">Speed in metres per second.</param>
    public LinearAxis(double stroke, double speed)
    {
        if (!(stroke > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(stroke), "Stroke must be positive.");
        }

        if (!(speed > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be positive.");
        }

        _stroke = stroke;
        _speed = speed;
    }

    public double Stroke => _stroke;

    /// <summary>Command: true travels out, false travels back.</summary>
    public bool Extend { get; set; }

    /// <summary>Position along the stroke, 0 = start.</summary>
    public double Position { get; private set; }

    /// <summary>True when the axis moved during the last step.</summary>
    public bool Moving { get; private set; }

    /// <param name="deltaSeconds">Length of the step.</param>
    /// <param name="limit">
    /// Furthest position the axis can reach on the way out during this step, e.g. where the
    /// gripper touches an item. It never pushes the axis back and does not affect the way in.
    /// </param>
    public void Tick(double deltaSeconds, double limit = double.PositiveInfinity)
    {
        double maxStep = _speed * deltaSeconds;
        double step;
        if (Extend)
        {
            double target = Math.Min(_stroke, Math.Max(limit, Position));
            step = Math.Min(target - Position, maxStep);
        }
        else
        {
            step = -Math.Min(Position, maxStep);
        }

        Position = Math.Clamp(Position + step, 0.0, _stroke);
        Moving = Math.Abs(step) > 1e-9;
    }

    public void Reset()
    {
        Extend = false;
        Position = 0.0;
        Moving = false;
    }
}
