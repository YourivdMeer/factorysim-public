namespace FactorySim.Core;

/// <summary>
/// Positioning bars: two clamps either side of a conveyor that close together on whatever stands
/// between them and so put it in the middle. Both blades move the same distance, so the item ends
/// up on the centre line when both touch it. While commanded they close until they touch an item or
/// reach the end of their stroke; otherwise they open again.
///
/// <see cref="Closed"/> means "the clamps stopped closing": on an item, or at the end of the stroke
/// when there was nothing between them. It does not say which; the PLC knows from its own sensor.
/// </summary>
public sealed class PositioningClamp
{
    private readonly LinearAxis _axis;
    private readonly double _openHalfGap;

    /// <param name="openHalfGap">Distance of the inner face of each blade from the centre line when open, in metres.</param>
    /// <param name="stroke">How far each blade travels towards the centre, in metres. Smaller than <paramref name="openHalfGap"/>.</param>
    /// <param name="speed">Blade speed in metres per second.</param>
    public PositioningClamp(double openHalfGap, double stroke, double speed)
    {
        if (!(stroke > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(stroke), "Stroke must be positive.");
        }

        if (!(openHalfGap > stroke))
        {
            throw new ArgumentOutOfRangeException(nameof(openHalfGap), "The blades must not meet before the end of the stroke.");
        }

        _axis = new LinearAxis(stroke, speed);
        _openHalfGap = openHalfGap;
    }

    /// <summary>Command: true closes the clamps, false opens them.</summary>
    public bool Close
    {
        get => _axis.Extend;
        set => _axis.Extend = value;
    }

    /// <summary>How far each blade has moved in from its open position, in metres.</summary>
    public double Position => _axis.Position;

    /// <summary>Distance of the inner face of each blade from the centre line, in metres.</summary>
    public double HalfGap => _openHalfGap - Position;

    /// <summary>Limit switch: the clamps are fully open.</summary>
    public bool Open => Position <= PusherLogic.LimitTolerance;

    /// <summary>The clamps are commanded to close and have come to rest, on an item or at the end of the stroke.</summary>
    public bool Closed => Close && !_axis.Moving && Position > PusherLogic.LimitTolerance;

    /// <param name="deltaSeconds">Length of the step.</param>
    /// <param name="itemHalfWidth">
    /// Half the width, across the conveyor, of the widest item between the blades; null when there is none.
    /// The blades never close further than that, and never move back for it.
    /// </param>
    public void Tick(double deltaSeconds, double? itemHalfWidth = null)
    {
        double limit = itemHalfWidth is { } half ? _openHalfGap - half : double.PositiveInfinity;
        _axis.Tick(deltaSeconds, limit);
    }

    public void Reset() => _axis.Reset();

    /// <summary>
    /// Half the extent of a box across the clamps. The box has the given size along its own axes;
    /// the direction across the conveyor, a unit vector, is given in the box's own frame (x, y, z).
    /// </summary>
    public static double HalfExtent(double sizeX, double sizeY, double sizeZ, double axisX, double axisY, double axisZ) =>
        0.5 * (Math.Abs(axisX) * sizeX + Math.Abs(axisY) * sizeY + Math.Abs(axisZ) * sizeZ);
}
