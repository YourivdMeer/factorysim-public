using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Monostable pusher, or with a bistable valve. The head travels along local +Z; position and limit switches
/// come from <see cref="PusherLogic"/>.
/// </summary>
public partial class Pusher : AnimatableBody3D
{
    private static readonly Vector3 HeadSize = new(0.6f, 0.25f, 0.08f);

    private readonly PusherLogic _logic;
    private readonly float _stroke;
    private Vector3 _restPosition;

    public Pusher()
        : this(stroke: 0.88f, speed: 1.0f)
    {
    }

    public Pusher(float stroke, float speed, bool bistable = false)
    {
        _stroke = stroke;
        _logic = new PusherLogic(stroke, speed) { Bistable = bistable };
    }

    /// <summary>True with a bistable valve (plan 12): driven by <see cref="ExtendCoil"/> and <see cref="RetractCoil"/>.</summary>
    public bool Bistable => _logic.Bistable;

    public bool ExtendCoil
    {
        get => _logic.ExtendCoil;
        set => _logic.ExtendCoil = value;
    }

    public bool RetractCoil
    {
        get => _logic.RetractCoil;
        set => _logic.RetractCoil = value;
    }

    /// <summary>Bistable: both coils are on.</summary>
    public bool Conflict => _logic.Conflict;

    /// <summary>Position of the head along its stroke, in metres from the back.</summary>
    public double HeadPosition => _logic.Position;

    public bool Extend
    {
        get => _logic.Extend;
        set => _logic.Extend = value;
    }

    public bool AtBack => _logic.AtBack;

    public bool AtFront => _logic.AtFront;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        SyncToPhysics = true;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.3f };
        _restPosition = Position;

        AddChild(Shapes.BoxCollider(HeadSize));

        // Plate and rod ride along with this body. The cylinder and its stand must stay put,
        // so that part of the model ignores the body's motion.
        Node3D model = Models.Instantiate("pusher");
        AddChild(model);
        MeshInstance3D body = Models.Part(model, "PusherBody");
        body.TopLevel = true;
        body.GlobalTransform = GlobalTransform;
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Tick(delta);
        Position = _restPosition + Basis.Z.Normalized() * (float)_logic.Position;
    }

    public void Reset()
    {
        _logic.Reset();
        Position = _restPosition;
    }
}
