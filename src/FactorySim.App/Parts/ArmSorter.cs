using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Pivot arm sorter: a post beside a conveyor with an arm that swings across the belt while
/// its tag is on. The node origin is the pivot, at transport height. At rest the arm points
/// along local +X (downstream, alongside the belt); it swings towards local +Z. The belt on
/// the arm face runs towards the tip while the arm is commanded out, so items that reach the
/// arm are carried off the far side of the conveyor.
/// Timing reuses <see cref="PusherLogic"/>, with the swing angle as the stroke.
/// </summary>
public partial class ArmSorter : AnimatableBody3D
{
    private const float ArmHeight = 0.16f;
    private const float ArmCentreHeight = 0.11f;

    /// <summary>Collider thickness; a little thicker than the model so items cannot slip through.</summary>
    private const float ArmThickness = 0.07f;

    private readonly PusherLogic _logic;
    private Vector3 _restRotation;

    public ArmSorter()
        : this(angleDegrees: 45.0f, swingDegreesPerSecond: 120.0f)
    {
    }

    public ArmSorter(float angleDegrees, float swingDegreesPerSecond)
    {
        _logic = new PusherLogic(Mathf.DegToRad(angleDegrees), Mathf.DegToRad(swingDegreesPerSecond));
    }

    /// <summary>Arm length from the pivot, in metres. Must match the model.</summary>
    public float Length { get; set; } = 1.2f;

    /// <summary>Speed of the belt on the arm face, in metres per second.</summary>
    public float BeltSpeed { get; set; } = 0.6f;

    /// <summary>Name of the model in the library; it must have an Arm part.</summary>
    public string ModelName { get; set; } = "pivot_arm_sorter";

    /// <summary>True swings the arm across the belt, false swings it back.</summary>
    public bool Out
    {
        get => _logic.Extend;
        set => _logic.Extend = value;
    }

    public bool IsOut => _logic.AtFront;

    public bool IsBack => _logic.AtBack;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        SyncToPhysics = true;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f };
        _restRotation = Rotation;

        AddChild(Shapes.BoxCollider(
            new Vector3(Length, ArmHeight, ArmThickness),
            new Vector3(Length / 2.0f, ArmCentreHeight, 0.0f)));

        // The arm rides along with this body; the post stays where it is.
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        foreach (Node child in model.FindChildren("*_Static", "MeshInstance3D", recursive: true, owned: false))
        {
            var post = (MeshInstance3D)child;
            post.TopLevel = true;
            post.GlobalTransform = GlobalTransform;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Tick(delta);
        Rotation = _restRotation - new Vector3(0.0f, (float)_logic.Position, 0.0f);
        ConstantLinearVelocity = Out ? GlobalBasis.X.Normalized() * BeltSpeed : Vector3.Zero;
    }

    public void Reset()
    {
        _logic.Reset();
        Rotation = _restRotation;
        ConstantLinearVelocity = Vector3.Zero;
    }
}
