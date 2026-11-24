using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Palletizer. The node origin is on the floor at the centre of the frame; boxes arrive at
/// plate height on a driven lane at local z = -0.5 and queue against the end stop. The pusher
/// shoves each row of three sideways onto the layer plate; a second push moves the first row
/// on, which gives a layer of two rows. The plate is two leaves that slide apart, dropping the
/// layer between the fences onto the pallet on the lifting table underneath. The table jogs up
/// and down and has rollers that bring the pallet in and out along local +X.
///
/// The sizes below must match the model (see _palletizer in the asset generator). The frame
/// has no collider: it is visual only, like the other models.
/// </summary>
public partial class Palletizer : Node3D
{
    private const float LaneZ = -0.5f;
    private const float FenceHeight = 0.145f;
    private const float TableBodyHeight = 0.2f;

    private readonly PusherLogic _pusher;
    private readonly PusherLogic _leaves;
    private readonly JogAxis _table;

    private StaticBody3D _lane = null!;
    private AnimatableBody3D _pusherBody = null!;
    private AnimatableBody3D _leafA = null!;
    private AnimatableBody3D _leafB = null!;
    private AnimatableBody3D _tableBody = null!;

    private Node3D _pusherModel = null!;
    private Node3D _leafAModel = null!;
    private Node3D _leafBModel = null!;
    private Node3D _tableModel = null!;
    private Vector3 _pusherModelRest;
    private Vector3 _leafAModelRest;
    private Vector3 _leafBModelRest;
    private Vector3 _tableModelRest;

    public Palletizer()
        : this(pushStroke: 0.37f, pushSpeed: 0.4f, leafStroke: 0.5f, leafSpeed: 1.0f, tableTravel: 1.25f, tableSpeed: 0.3f)
    {
    }

    public Palletizer(float pushStroke, float pushSpeed, float leafStroke, float leafSpeed, float tableTravel, float tableSpeed)
    {
        _pusher = new PusherLogic(pushStroke, pushSpeed);
        _leaves = new PusherLogic(leafStroke, leafSpeed);
        _table = new JogAxis(tableTravel, tableSpeed);
    }

    /// <summary>Name of the model in the library; it must have Pusher, Leaf_A, Leaf_B and ElevatorTable parts.</summary>
    public string ModelName { get; set; } = "palletizer";

    /// <summary>Height of the layer plate above the floor, in metres.</summary>
    public float DeckHeight { get; set; } = 1.9f;

    /// <summary>Height of the table rollers above the floor with the table down, in metres.</summary>
    public float BaseHeight { get; set; } = 0.5f;

    public float TableLength { get; set; } = 2.5f;

    public float TableWidth { get; set; } = 1.0f;

    public float FeedSpeed { get; set; } = 0.4f;

    public float RollSpeed { get; set; } = 0.45f;

    /// <summary>Runs the infeed lane.</summary>
    public bool Feed { get; set; }

    /// <summary>True pushes the row onto the plate, false retracts.</summary>
    public bool Push
    {
        get => _pusher.Extend;
        set => _pusher.Extend = value;
    }

    /// <summary>True opens the layer plate, false closes it.</summary>
    public bool Open
    {
        get => _leaves.Extend;
        set => _leaves.Extend = value;
    }

    public bool Up
    {
        get => _table.Up;
        set => _table.Up = value;
    }

    public bool Down
    {
        get => _table.Down;
        set => _table.Down = value;
    }

    /// <summary>Runs the table rollers along local +X.</summary>
    public bool Rollers { get; set; }

    public bool PusherBack => _pusher.AtBack;

    public bool PusherFront => _pusher.AtFront;

    public bool PlateClosed => _leaves.AtBack;

    public bool PlateOpen => _leaves.AtFront;

    public bool TableDown => _table.AtMin;

    /// <summary>A box is at the last place of the row on the infeed lane.</summary>
    public bool RowFull { get; private set; }

    /// <summary>The pallet or the stack on it is just under the plate.</summary>
    public bool StackHigh { get; private set; }

    /// <summary>A pallet is in place on the table.</summary>
    public bool PalletPresent { get; private set; }

    /// <summary>Height of the table rollers above the floor right now, in metres.</summary>
    public float TableHeight => BaseHeight + (float)_table.Position;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _pusherModel = Models.Part(model, "Pusher");
        _leafAModel = Models.Part(model, "Leaf_A");
        _leafBModel = Models.Part(model, "Leaf_B");
        _tableModel = Models.Part(model, "ElevatorTable");
        _pusherModelRest = _pusherModel.Position;
        _leafAModelRest = _leafAModel.Position;
        _leafBModelRest = _leafBModel.Position;
        _tableModelRest = _tableModel.Position;

        float deck = DeckHeight;
        float fenceY = deck + 0.005f + FenceHeight / 2.0f;

        _lane = new StaticBody3D
        {
            Name = "Lane",
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f },
        };
        _lane.AddChild(Shapes.BoxCollider(new Vector3(1.71f, 0.04f, 0.365f), new Vector3(-0.395f, deck - 0.02f, -0.5075f)));
        AddChild(_lane);

        var fences = new StaticBody3D
        {
            Name = "Fences",
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.1f },
        };
        fences.AddChild(Shapes.BoxCollider(new Vector3(0.04f, FenceHeight, 1.05f), new Vector3(0.48f, fenceY, -0.165f)));     // end stop
        fences.AddChild(Shapes.BoxCollider(new Vector3(0.04f, FenceHeight, 0.685f), new Vector3(-0.52f, fenceY, 0.0175f)));   // back fence
        fences.AddChild(Shapes.BoxCollider(new Vector3(1.04f, FenceHeight, 0.04f), new Vector3(-0.02f, fenceY, 0.34f)));      // side fence
        fences.AddChild(Shapes.BoxCollider(new Vector3(0.71f, FenceHeight, 0.02f), new Vector3(-0.895f, fenceY, -0.33f)));    // lane guides
        fences.AddChild(Shapes.BoxCollider(new Vector3(0.79f, FenceHeight, 0.02f), new Vector3(-0.855f, fenceY, -0.68f)));
        AddChild(fences);

        _pusherBody = Moving("PusherBody", 0.3f);
        _pusherBody.AddChild(Shapes.BoxCollider(new Vector3(0.88f, 0.15f, 0.04f)));
        _leafA = Moving("LeafA", 0.3f);
        _leafA.AddChild(Shapes.BoxCollider(new Vector3(0.5f, 0.03f, 0.685f)));
        _leafB = Moving("LeafB", 0.3f);
        _leafB.AddChild(Shapes.BoxCollider(new Vector3(0.5f, 0.03f, 0.685f)));
        _tableBody = Moving("TableBody", 1.0f);
        _tableBody.AddChild(Shapes.BoxCollider(
            new Vector3(TableLength, TableBodyHeight, TableWidth), new Vector3(0.0f, -TableBodyHeight / 2.0f, 0.0f)));
        MoveParts();
    }

    public override void _PhysicsProcess(double delta)
    {
        _pusher.Tick(delta);
        _leaves.Tick(delta);
        _table.Tick(delta);
        MoveParts();

        Vector3 along = GlobalBasis.X.Normalized();
        _lane.ConstantLinearVelocity = Feed ? along * FeedSpeed : Vector3.Zero;
        _tableBody.ConstantLinearVelocity = Rollers ? along * RollSpeed : Vector3.Zero;

        float deck = DeckHeight;
        RowFull = Sees(new Vector3(-0.30f, deck + 0.1f, LaneZ - 0.16f), new Vector3(-0.30f, deck + 0.1f, LaneZ + 0.16f));
        StackHigh = Sees(new Vector3(-0.7f, deck - 0.05f, 0.15f), new Vector3(0.7f, deck - 0.05f, 0.15f));
        float palletY = TableHeight + 0.07f;
        PalletPresent = Sees(new Vector3(0.55f, palletY, -0.6f), new Vector3(0.55f, palletY, 0.6f));
    }

    public void Reset()
    {
        _pusher.Reset();
        _leaves.Reset();
        _table.Reset();
        Feed = false;
        Rollers = false;
        RowFull = false;
        StackHigh = false;
        PalletPresent = false;
        if (IsInsideTree())
        {
            MoveParts();
            _lane.ConstantLinearVelocity = Vector3.Zero;
            _tableBody.ConstantLinearVelocity = Vector3.Zero;
        }
    }

    private AnimatableBody3D Moving(string name, float friction)
    {
        var body = new AnimatableBody3D
        {
            Name = name,
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            SyncToPhysics = true,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = friction },
        };
        AddChild(body);
        return body;
    }

    private void MoveParts()
    {
        var push = new Vector3(0.0f, 0.0f, (float)_pusher.Position);
        var slide = new Vector3((float)_leaves.Position, 0.0f, 0.0f);
        var lift = new Vector3(0.0f, (float)_table.Position, 0.0f);

        _pusherBody.Position = new Vector3(0.0f, DeckHeight + 0.085f, -0.69f) + push;
        _leafA.Position = new Vector3(-0.25f, DeckHeight - 0.015f, 0.0175f) - slide;
        _leafB.Position = new Vector3(0.25f, DeckHeight - 0.015f, 0.0175f) + slide;
        _tableBody.Position = new Vector3(0.0f, BaseHeight, 0.0f) + lift;

        _pusherModel.Position = _pusherModelRest + push;
        _leafAModel.Position = _leafAModelRest - slide;
        _leafBModel.Position = _leafBModelRest + slide;
        _tableModel.Position = _tableModelRest + lift;
    }

    /// <summary>True when a box or pallet crosses the line between two points given in local space.</summary>
    private bool Sees(Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(GlobalTransform * from, GlobalTransform * to, Layers.Boxes);
        query.HitFromInside = true;
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }
}
