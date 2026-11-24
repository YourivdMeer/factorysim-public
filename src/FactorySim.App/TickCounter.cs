using Godot;

namespace FactorySim.App;

/// <summary>
/// Counts the physics ticks it receives. Placed under World (pausable), it shows how many ticks
/// the world really ran; used by the time-scale self-test.
/// </summary>
public partial class TickCounter : Node
{
    public long Ticks { get; private set; }

    public override void _PhysicsProcess(double delta) => Ticks++;
}
