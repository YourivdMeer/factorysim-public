namespace FactorySim.Core;

/// <summary>
/// The forks of a stacker crane: they run out to the left while <see cref="Left"/> is on, out to
/// the right while <see cref="Right"/> is on and back to the middle when neither is. Both at once
/// do nothing. Position is 0 in the middle, negative to the left.
/// </summary>
public sealed class ForkAxis
{
    private readonly double _stroke;
    private readonly double _speed;

    /// <param name="stroke">Travel from the middle to either end, in metres.</param>
    /// <param name="speed">Speed in metres per second.</param>
    public ForkAxis(double stroke, double speed)
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

    public bool Left { get; set; }

    public bool Right { get; set; }

    public double Position { get; private set; }

    public bool Moving { get; private set; }

    public bool AtMiddle => Math.Abs(Position) <= PusherLogic.LimitTolerance;

    public bool AtLeftEnd => Position <= -_stroke + PusherLogic.LimitTolerance;

    public bool AtRightEnd => Position >= _stroke - PusherLogic.LimitTolerance;

    public void Tick(double deltaSeconds)
    {
        double before = Position;
        double maxStep = _speed * deltaSeconds;
        if (Left && Right)
        {
            // Contradicting commands: stay where they are.
        }
        else if (Left || Right)
        {
            double end = Left ? -_stroke : _stroke;
            Position += Math.Clamp(end - Position, -maxStep, maxStep);
        }
        else
        {
            Position += Math.Clamp(-Position, -maxStep, maxStep);
        }

        Moving = Math.Abs(Position - before) > 1e-12;
    }

    public void Reset()
    {
        Left = false;
        Right = false;
        Position = 0.0;
        Moving = false;
    }
}
