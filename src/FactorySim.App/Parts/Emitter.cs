using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Spawns boxes at its position. Timing and the low/high sequence come from
/// <see cref="EmitterLogic"/>; this node only checks the spawn volume and creates the body.
/// The model is a portal with a hopper; its lamp is green while the emitter is enabled.
/// </summary>
public partial class Emitter : Node3D
{
    private static readonly Color LampColour = new(0.21f, 0.94f, 0.42f);

    private readonly EmitterLogic _logic;
    private Area3D? _volume;
    private StandardMaterial3D? _lampMaterial;
    private bool _lampOn;

    public Emitter()
        : this(intervalSeconds: 2.5, seed: 1)
    {
    }

    public Emitter(double intervalSeconds, int seed, double highRatio = 0.5)
    {
        _logic = new EmitterLogic(intervalSeconds, seed, highRatio);
    }

    /// <summary>Node the spawned boxes are added to. Must be set before the first spawn.</summary>
    public Node? BoxParent { get; set; }

    /// <summary>Name of the model in the library; it must have an EmitterLamp part.</summary>
    public string ModelName { get; set; } = "emitter_gantry";

    /// <summary>The volume that must be empty before the next item appears, in metres.</summary>
    public Vector3 VolumeSize { get; set; } = new(0.5f, 0.6f, 0.5f);

    /// <summary>Library item to emit instead of the two boxes, e.g. "pallet"; null emits boxes.</summary>
    public string? ItemModel { get; set; }

    public Vector3 ItemSize { get; set; }

    public float ItemMass { get; set; } = 2.0f;

    public bool Enabled
    {
        get => _logic.Enabled;
        set => _logic.Enabled = value;
    }

    public int EmittedCount => _logic.EmittedCount;

    /// <summary>Raised with every item right after it has been added to the scene.</summary>
    public event System.Action<Box>? Spawned;

    public override void _Ready()
    {
        _volume = new Area3D
        {
            Name = "SpawnVolume",
            CollisionLayer = 0,
            CollisionMask = Layers.Boxes,
            Monitoring = true,
            Monitorable = false,
            Position = new Vector3(0.0f, VolumeSize.Y / 2.0f, 0.0f),
        };
        _volume.AddChild(Shapes.BoxCollider(VolumeSize));
        AddChild(_volume);

        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _lampMaterial = Models.OwnMaterial(Models.Part(model, "EmitterLamp"));
        Models.SetGlow(_lampMaterial, LampColour, false);
    }

    public override void _Process(double delta)
    {
        if (_lampMaterial is not null && _lampOn != _logic.Enabled)
        {
            _lampOn = _logic.Enabled;
            Models.SetGlow(_lampMaterial, LampColour, _lampOn);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_volume is null || BoxParent is null)
        {
            return;
        }

        BoxKind? kind = _logic.Tick(delta, _volume.HasOverlappingBodies());
        if (kind is null)
        {
            return;
        }

        Box box = ItemModel is null
            ? new Box { Kind = kind.Value }
            : new Box { ItemModel = ItemModel, ItemSize = ItemSize, ItemMass = ItemMass };
        BoxParent.AddChild(box);
        // Drop from just above the belt so the box settles instead of spawning in contact.
        box.GlobalTransform = new Transform3D(GlobalBasis.Orthonormalized(), GlobalPosition + new Vector3(0.0f, box.Height / 2.0f + 0.02f, 0.0f));
        Spawned?.Invoke(box);
    }

    public void Reset()
    {
        _logic.Enabled = false;
        _logic.Reset();
    }
}
