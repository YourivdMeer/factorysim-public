namespace FactorySim.Core;

/// <summary>
/// The knob of a potentiometer: a value from 0 to <see cref="Maximum"/> that the operator turns in
/// steps of a twentieth. The knob turns 270°, from 135° left of straight up to 135° right.
/// </summary>
public sealed class KnobLogic
{
    public const int Maximum = 1000;

    /// <summary>One click or one notch of the mouse wheel.</summary>
    public const int StepSize = 50;

    /// <summary>Angle of the knob at 0, in degrees; positive turns clockwise as seen from the front.</summary>
    public const double MinAngle = -135.0;

    public const double MaxAngle = 135.0;

    private readonly int _start;

    public KnobLogic(int start = 0)
    {
        _start = Math.Clamp(start, 0, Maximum);
        Value = _start;
    }

    public int Value { get; private set; }

    /// <summary>Angle of the pointer from straight up, clockwise positive.</summary>
    public double AngleDegrees => MinAngle + (MaxAngle - MinAngle) * Value / Maximum;

    /// <summary>Turns the knob by a number of steps: positive to the right (higher), negative to the left.</summary>
    public void Step(int steps) => Set(Value + steps * StepSize);

    public void Set(int value) => Value = Math.Clamp(value, 0, Maximum);

    public void Reset() => Value = _start;
}
