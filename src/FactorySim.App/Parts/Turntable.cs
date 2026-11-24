using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Turntable: a round roller deck that makes a quarter turn about the vertical axis while
/// its turn tag is on, and turns back when the tag drops. The node origin is the centre of
/// the deck surface. In the home position the rollers carry along local +X; turned, they
/// carry along local -Z (to the left). The rollers can run forward or in reverse.
/// Timing reuses <see cref="PusherLogic"/>, with the turn angle as the stroke.
/// </summary>
public partial class Turntable : AnimatableBody3D
{
    private const float BodyHeight = 0.2f;

    private readonly PusherLogic _logic;
    private Vector3 _restRotation;

    public Turntable()
        : this(angleDegrees: 90.0f, turnDegreesPerSecond: 60.0f)
    {
    }

    public Turntable(float angleDegrees, float turnDegreesPerSecond)
    {
        _logic = new PusherLogic(Mathf.DegToRad(angleDegrees), Mathf.DegToRad(turnDegreesPerSecond));
    }

    /// <summary>Radius of the deck, in metres. Must match the model.</summary>
    public float Radius { get; set; } = 0.9f;

    /// <summary>Surface speed of the rollers, in metres per second.</summary>
    public float Speed { get; set; } = 0.45f;

    /// <summary>Name of the model in the library; it must have a Deck part.</summary>
    public string ModelName { get; set; } = "turntable";

    public bool Forward { get; set; }

    public bool Reverse { get; set; }

    /// <summary>True turns the deck, false turns it back.</summary>
    public bool Turn
    {
        get => _logic.Extend;
        set => _logic.Extend = value;
    }

    /// <summary>Limit switch: the deck is in its home position.</summary>
    public bool AtHome => _logic.AtBack;

    /// <summary>Limit switch: the deck has made its full turn.</summary>
    public bool AtTurned => _logic.AtFront;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        SyncToPhysics = true;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f };
        _restRotation = Rotation;

        AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = Radius, Height = BodyHeight },
            Position = new Vector3(0.0f, -BodyHeight / 2.0f, 0.0f),
        });

        // The deck rides along with this body; the base stays where it is.
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        foreach (Node child in model.FindChildren("*_Static", "MeshInstance3D", recursive: true, owned: false))
        {
            var foot = (MeshInstance3D)child;
            foot.TopLevel = true;
            foot.GlobalTransform = GlobalTransform;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Tick(delta);
        Rotation = _restRotation + new Vector3(0.0f, (float)_logic.Position, 0.0f);
        ConstantLinearVelocity = GlobalBasis.X.Normalized() * Speed * WheelSorterLogic.Roll(Forward, Reverse);
    }

    public void Reset()
    {
        _logic.Reset();
        Forward = false;
        Reverse = false;
        Rotation = _restRotation;
        ConstantLinearVelocity = Vector3.Zero;
    }
}
