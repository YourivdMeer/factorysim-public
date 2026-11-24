using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Elevator: a roller platform that travels between a lower and an upper level. The node
/// origin is on the floor at the centre of the frame. The platform goes up while its tag is
/// on and comes down when the tag drops; its rollers carry along local +X, forward or in
/// reverse. Timing reuses <see cref="PusherLogic"/>, with the travel as the stroke, which also
/// gives the two limit switches.
///
/// The frame has no collider: it is visual only, like the other models.
/// </summary>
public partial class Elevator : Node3D
{
    private const float BodyHeight = 0.2f;

    private readonly PusherLogic _logic;
    private AnimatableBody3D _platform = null!;
    private Node3D _platformModel = null!;
    private Vector3 _platformModelRest;

    public Elevator()
        : this(travel: 3.0f, speed: 0.5f)
    {
    }

    public Elevator(float travel, float speed)
    {
        _logic = new PusherLogic(travel, speed);
    }

    /// <summary>Height of the roller surface above the floor at the lower level, in metres.</summary>
    public float BaseHeight { get; set; } = 0.5f;

    /// <summary>Platform length along local X and width along local Z, in metres. Must match the model.</summary>
    public float Length { get; set; } = 1.6f;

    public float Width { get; set; } = 1.4f;

    /// <summary>Surface speed of the rollers, in metres per second.</summary>
    public float RollSpeed { get; set; } = 0.45f;

    /// <summary>Name of the model in the library; it must have a Platform part.</summary>
    public string ModelName { get; set; } = "elevator";

    /// <summary>True sends the platform up, false brings it down.</summary>
    public bool Up
    {
        get => _logic.Extend;
        set => _logic.Extend = value;
    }

    public bool Forward { get; set; }

    public bool Reverse { get; set; }

    public bool AtBottom => _logic.AtBack;

    public bool AtTop => _logic.AtFront;

    /// <summary>Height of the roller surface above the floor right now, in metres.</summary>
    public float SurfaceHeight => BaseHeight + (float)_logic.Position;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _platformModel = Models.Part(model, "Platform");
        _platformModelRest = _platformModel.Position;

        _platform = new AnimatableBody3D
        {
            Name = "PlatformBody",
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            SyncToPhysics = true,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f },
            Position = new Vector3(0.0f, BaseHeight, 0.0f),
        };
        _platform.AddChild(Shapes.BoxCollider(new Vector3(Length, BodyHeight, Width), new Vector3(0.0f, -BodyHeight / 2.0f, 0.0f)));
        AddChild(_platform);
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Tick(delta);
        MovePlatform();
        _platform.ConstantLinearVelocity = GlobalBasis.X.Normalized() * RollSpeed * WheelSorterLogic.Roll(Forward, Reverse);
    }

    public void Reset()
    {
        _logic.Reset();
        Forward = false;
        Reverse = false;
        if (IsInsideTree())
        {
            MovePlatform();
            _platform.ConstantLinearVelocity = Vector3.Zero;
        }
    }

    private void MovePlatform()
    {
        var lift = new Vector3(0.0f, (float)_logic.Position, 0.0f);
        _platform.Position = new Vector3(0.0f, BaseHeight, 0.0f) + lift;
        _platformModel.Position = _platformModelRest + lift;
    }
}
