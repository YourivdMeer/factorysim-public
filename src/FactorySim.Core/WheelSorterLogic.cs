namespace FactorySim.Core;

/// <summary>
/// Which way a pop-up wheel sorter (or, at 90 degrees, a chain transfer) carries an item. "Run" drives the deck straight on;
/// "left" or "right" raises the wheels, which then carry the item off at the wheel angle,
/// whether or not "run" is on. Both sides at once cancel out.
/// </summary>
public static class WheelSorterLogic
{
    /// <summary>
    /// Sense of rotation of rollers with a forward and a reverse command:
    /// 1 forward, -1 in reverse, 0 when neither or both are commanded.
    /// </summary>
    public static int Roll(bool forward, bool reverse) => forward == reverse ? 0 : forward ? 1 : -1;

    /// <summary>True while the wheels are raised: exactly one side is commanded.</summary>
    public static bool WheelsUp(bool left, bool right) => left != right;

    /// <summary>
    /// Direction of the surface as a unit vector (or zero when it stands still):
    /// Forward along the direction of travel, ToLeft positive towards the left-hand side.
    /// </summary>
    public static (double Forward, double ToLeft) Direction(bool run, bool left, bool right, double angleDegrees)
    {
        if (WheelsUp(left, right))
        {
            double angle = angleDegrees * Math.PI / 180.0;
            return (Math.Cos(angle), left ? Math.Sin(angle) : -Math.Sin(angle));
        }

        return run ? (1.0, 0.0) : (0.0, 0.0);
    }
}
