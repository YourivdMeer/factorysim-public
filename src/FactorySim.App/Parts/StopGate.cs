using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Stop blade or roller stop: a blade that rises through the transport surface while its
/// tag is on and holds boxes back. The node origin is at surface scene; the blade travels
/// along local +Y. Timing reuses <see cref="PusherLogic"/> (a stroke at constant speed).
/// </summary>
public partial class StopGate : AnimatableBody3D
{
    /// <summary>Collider thickness. Thicker than the model's blade so fast boxes cannot tunnel through.</summary>
    private const float ColliderThickness = 0.04f;

    private readonly PusherLogic _logic;
    private Vector3 _restPosition;

    public StopGate()
        : this(stroke: 0.12f, speed: 0.6f)
    {
    }

    public StopGate(float stroke, float speed)
    {
        _logic = new PusherLogic(stroke, speed);
    }

    /// <summary>Name of the model in the library; it must have a Blade part.</summary>
    public string ModelName { get; set; } = "stop_blade";

    /// <summary>Blade size across the conveyor and in height, and the height of its centre when lowered.</summary>
    public float BladeWidth { get; set; } = 0.72f;

    public float BladeHeight { get; set; } = 0.14f;

    public float BladeCentre { get; set; } = -0.075f;

    /// <summary>True raises the blade, false lowers it.</summary>
    public bool Raised
    {
        get => _logic.Extend;
        set => _logic.Extend = value;
    }

    public bool IsUp => _logic.AtFront;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        SyncToPhysics = true;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.3f };
        _restPosition = Position;

        AddChild(Shapes.BoxCollider(new Vector3(ColliderThickness, BladeHeight, BladeWidth), new Vector3(0.0f, BladeCentre, 0.0f)));

        // The blade rides along with this body; the housing below stays where it is.
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        foreach (Node child in model.FindChildren("*_Static", "MeshInstance3D", recursive: true, owned: false))
        {
            var housing = (MeshInstance3D)child;
            housing.TopLevel = true;
            housing.GlobalTransform = GlobalTransform;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Tick(delta);
        Position = _restPosition + Basis.Y.Normalized() * (float)_logic.Position;
    }

    public void Reset()
    {
        _logic.Reset();
        Position = _restPosition;
    }
}
