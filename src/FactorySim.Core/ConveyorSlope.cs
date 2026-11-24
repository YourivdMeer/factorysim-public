namespace FactorySim.Core;

/// <summary>
/// Geometry of a belt that climbs. <paramref name="Length"/> is the horizontal length and
/// <paramref name="Rise"/> the height gained over it, both in metres; a rise of 0 is a flat belt.
/// The belt runs along local +X and climbs towards +X. The frame is the conveyor's own:
/// x along the travel, y up.
/// </summary>
public readonly record struct ConveyorSlope(double Length, double Rise)
{
    /// <summary>Angle of the surface above the horizontal, in radians.</summary>
    public double Angle => Math.Atan2(Rise, Length);

    /// <summary>Length of the surface measured along the slope, in metres.</summary>
    public double SurfaceLength => Math.Sqrt(Length * Length + Rise * Rise);

    /// <summary>
    /// Unit vector in which an item is carried: along the surface, up the slope or, when
    /// <paramref name="reverse"/> is set, down it.
    /// </summary>
    public (double X, double Y) Direction(bool reverse)
    {
        double sign = reverse ? -1.0 : 1.0;
        return (sign * Math.Cos(Angle), sign * Math.Sin(Angle));
    }

    /// <summary>
    /// Where the centre of the collider lies, seen from a node origin that is half a thickness
    /// straight below the surface point. The collider is tilted with the slope and hangs below
    /// the surface along its normal, so its top face passes through that surface point.
    /// </summary>
    public (double X, double Y) ColliderOffset(double thickness)
    {
        double half = thickness / 2.0;
        return (half * Math.Sin(Angle), half - half * Math.Cos(Angle));
    }
}
