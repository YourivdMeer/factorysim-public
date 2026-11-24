namespace FactorySim.Core;

/// <summary>
/// Kinematics of a pusher with a limit switch at each end of the stroke. Monostable (the default): extends
/// while commanded, a spring retracts it otherwise. <see cref="Bistable"/>: a valve with a coil for out and
/// one for in that keeps its last position when both coils are off, so the pusher goes on to the end it
/// was last sent to (plan 12).
/// </summary>
public sealed class PusherLogic
{
    /// <summary>How close to an end the head must be for its limit switch to close, in metres.</summary>
    public const double LimitTolerance = 0.005;

    private readonly double _stroke;
    private readonly double _speed;

    /// <param name="stroke">Travel from back to front, in metres.</param>
    /// <param name="speed">Head speed in metres per second.</param>
    public PusherLogic(double stroke, double speed)
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

    /// <summary>
    /// Command: true extends, false retracts. With <see cref="Bistable"/> this is the position of the valve,
    /// set by the coils on every tick.
    /// </summary>
    public bool Extend { get; set; }

    /// <summary>True for a bistable valve, driven by <see cref="ExtendCoil"/> and <see cref="RetractCoil"/>.</summary>
    public bool Bistable { get; init; }

    /// <summary>Bistable: the coil that switches the valve to "out".</summary>
    public bool ExtendCoil { get; set; }

    /// <summary>Bistable: the coil that switches the valve to "in".</summary>
    public bool RetractCoil { get; set; }

    /// <summary>Bistable: both coils are on. The valve keeps its last position.</summary>
    public bool Conflict { get; private set; }

    /// <summary>Head position along the stroke, 0 = back.</summary>
    public double Position { get; private set; }

    /// <summary>Head velocity during the last step, in metres per second.</summary>
    public double Velocity { get; private set; }

    public bool AtBack => Position <= LimitTolerance;

    public bool AtFront => Position >= _stroke - LimitTolerance;

    public void Tick(double deltaSeconds)
    {
        if (Bistable)
        {
            Conflict = ExtendCoil && RetractCoil;
            if (ExtendCoil != RetractCoil)
            {
                Extend = ExtendCoil;
            }
        }

        double target = Extend ? _stroke : 0.0;
        double maxStep = _speed * deltaSeconds;
        double step = Math.Clamp(target - Position, -maxStep, maxStep);

        Position = Math.Clamp(Position + step, 0.0, _stroke);
        Velocity = deltaSeconds > 0.0 ? step / deltaSeconds : 0.0;
    }

    public void Reset()
    {
        Extend = false;
        ExtendCoil = false;
        RetractCoil = false;
        Conflict = false;
        Position = 0.0;
        Velocity = 0.0;
    }
}
