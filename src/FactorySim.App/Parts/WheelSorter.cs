using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Pop-up wheel sorter or chain transfer: a short conveyor section that carries items straight
/// on, or off to the left or right at the wheel angle (90 degrees for a chain transfer).
/// Travel direction is local +X; left is local -Z.
/// The node origin is the centre of the deck surface. As with the other conveyors nothing
/// really moves: the deck reports a surface velocity in the direction <see cref="WheelSorterLogic"/> gives.
/// </summary>
public partial class WheelSorter : StaticBody3D
{
    private const float BodyHeight = 0.2f;


    private Node3D? _wheels;
    private Vector3 _wheelsRest;
    private bool _run;
    private bool _left;
    private bool _right;

    /// <summary>Deck length along local X, in metres. Must match the model.</summary>
    public float Length { get; set; } = 1.0f;

    /// <summary>Deck width along local Z, in metres. Must match the model.</summary>
    public float Width { get; set; } = 0.8f;

    /// <summary>Surface speed, in metres per second.</summary>
    public float Speed { get; set; } = 0.6f;

    /// <summary>Angle between straight on and the direction the raised wheels carry items, in degrees.</summary>
    public float Angle { get; set; } = 45.0f;

    /// <summary>How far the wheels or chains rise while they carry sideways, in metres.</summary>
    public float Lift { get; set; } = 0.012f;

    /// <summary>Name of the model in the library; it must have a Wheels or a Chains part.</summary>
    public string ModelName { get; set; } = "popup_wheel_sorter";

    public bool Run
    {
        get => _run;
        set
        {
            _run = value;
            Apply();
        }
    }

    public bool Left
    {
        get => _left;
        set
        {
            _left = value;
            Apply();
        }
    }

    public bool Right
    {
        get => _right;
        set
        {
            _right = value;
            Apply();
        }
    }

    /// <summary>Surface velocity in world space, as the physics engine sees it.</summary>
    public Vector3 SurfaceVelocity => ConstantLinearVelocity;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f };
        AddChild(Shapes.BoxCollider(new Vector3(Length, BodyHeight, Width), new Vector3(0.0f, -BodyHeight / 2.0f, 0.0f)));

        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _wheels = model.FindChild("Wheels", recursive: true, owned: false) as Node3D ?? Models.Part(model, "Chains");
        _wheelsRest = _wheels.Position;
        Apply();
    }

    public void Reset()
    {
        _run = false;
        _left = false;
        _right = false;
        Apply();
    }

    private void Apply()
    {
        if (!IsInsideTree() || _wheels is null)
        {
            return;
        }

        (double forward, double toLeft) = WheelSorterLogic.Direction(_run, _left, _right, Angle);
        var local = new Vector3((float)forward, 0.0f, -(float)toLeft);
        ConstantLinearVelocity = GlobalBasis.Orthonormalized() * local * Speed;
        _wheels.Position = _wheelsRest + (WheelSorterLogic.WheelsUp(_left, _right) ? new Vector3(0.0f, Lift, 0.0f) : Vector3.Zero);
    }
}
