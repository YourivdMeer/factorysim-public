namespace FactorySim.Core;

/// <summary>
/// A conveyor with two outputs (plan 12): one for forward and one for reverse. Both on is an error in the
/// PLC program: the belt stops and the scene reports the conflict.
/// </summary>
public static class ConveyorDrive
{
    public static (ConveyorDirection Direction, bool Conflict) Direction(bool forward, bool reverse) =>
        (forward, reverse) switch
        {
            (true, true) => (ConveyorDirection.Stopped, true),
            (true, false) => (ConveyorDirection.Forward, false),
            (false, true) => (ConveyorDirection.Backward, false),
            _ => (ConveyorDirection.Stopped, false),
        };
}
