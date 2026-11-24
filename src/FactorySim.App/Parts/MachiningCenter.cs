using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Machining center. The node origin is on the floor at the centre of the frame; the open side
/// is local -X. Parts ride in on a driven deck at local z = +Lane and stop against a blade at
/// the pick position. On a start the robot takes the part, loads it into the machine, waits
/// while the spindle works, and sets the result down on the deck at z = -Lane, which runs
/// outwards. Raw material comes out as a product base or lid; anything else comes out as it
/// went in. The sequence itself is <see cref="MachiningCycle"/>.
///
/// The sizes must match the model (see _machining_center in the asset generator). Frame,
/// machine and robot have no collider: they are visual only, like the other models.
/// </summary>
public partial class MachiningCenter : Node3D
{
    private const float DeckLength = 0.65f;
    private const float DeckCentreX = -1.175f;
    private const float DeckThickness = 0.04f;

    /// <summary>How fast a part that was gripped off-centre slides under the gripper, in m/s.</summary>
    private const float CentringSpeed = 0.5f;

    private readonly MachiningCycle _cycle;

    private StaticBody3D _entryDeck = null!;
    private StaticBody3D _exitDeck = null!;
    private Area3D _exitArea = null!;
    private Node3D _arm = null!;
    private Node3D _spindle = null!;
    private Vector3 _armRest;
    private Vector3 _spindleRest;
    private readonly (StandardMaterial3D Material, Color Colour)[] _lamps = new (StandardMaterial3D, Color)[3];
    private readonly bool[] _lampOn = new bool[3];

    private Box? _part;
    private Vector3 _partOffset;
    private Basis _partBasis;
    private float _partHeight = 0.15f;
    private float _shownHeight = 0.15f;
    private bool _wasHolding;
    private bool _wasMachined;
    private bool _makeLid;

    public MachiningCenter()
        : this(entryAngle: 127.864f, swingSpeed: 120.0f, liftTime: 0.4f, machineTime: 4.0f)
    {
    }

    public MachiningCenter(float entryAngle, float swingSpeed, float liftTime, float machineTime)
    {
        _cycle = new MachiningCycle(entryAngle, swingSpeed, liftTime, machineTime);
    }

    /// <summary>Name of the model in the library; it must have RobotArm, Spindle and three Lamp_ parts.</summary>
    public string ModelName { get; set; } = "machining_center";

    /// <summary>Node the finished parts are added to. Must be set before the first cycle.</summary>
    public Node? BoxParent { get; set; }

    /// <summary>Height of the decks and the machine table above the floor, in metres.</summary>
    public float Surface { get; set; } = 0.8f;

    /// <summary>Local X of the robot axis.</summary>
    public float BaseX { get; set; } = -0.55f;

    /// <summary>Distance from the robot axis to the gripper, in metres.</summary>
    public float Reach { get; set; } = 0.76f;

    /// <summary>Distance of the deck centre lines from the middle of the station, in metres.</summary>
    public float Lane { get; set; } = 0.6f;

    public float LaneWidth { get; set; } = 0.8f;

    /// <summary>Local X of the centre of the stop blade.</summary>
    public float StopX { get; set; } = -0.8715f;

    /// <summary>Height of the gripper pad above the floor in the model as built, in metres.</summary>
    public float TipHeight { get; set; } = 1.1f;

    /// <summary>How far the gripper lifts a part off the surface, in metres.</summary>
    public float Lift { get; set; } = 0.15f;

    /// <summary>Height of the tool tip above the floor with the spindle up, in metres.</summary>
    public float SpindleHeight { get; set; } = 1.35f;

    public float DeckSpeed { get; set; } = 0.4f;

    /// <summary>Start command: a cycle begins while this is on, the station is idle and a part is at the entry.</summary>
    public bool Start { get; set; }

    /// <summary>On makes a lid of the next part, off a base. Read when the cycle starts.</summary>
    public bool Lid { get; set; }

    public bool Busy => _cycle.Busy;

    /// <summary>A part lies against the stop at the pick position.</summary>
    public bool EntryPresent { get; private set; }

    /// <summary>A part lies on the exit deck.</summary>
    public bool ExitOccupied { get; private set; }

    public MachiningStep Step => _cycle.Step;

    /// <summary>Number of parts set down on the exit deck since the last reset.</summary>
    public int Finished { get; private set; }

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _arm = Models.Part(model, "RobotArm");
        _spindle = Models.Part(model, "Spindle");
        _armRest = _arm.Position;
        _spindleRest = _spindle.Position;
        string[] lampParts = { "Lamp_Green", "Lamp_Yellow", "Lamp_Red" };
        for (int i = 0; i < lampParts.Length; i++)
        {
            MeshInstance3D mesh = Models.Part(model, lampParts[i]);
            Color colour = Models.LampColour(mesh.Mesh.SurfaceGetMaterial(0)) ?? Colors.White;
            _lamps[i] = (Models.OwnMaterial(mesh), colour);
            Models.SetGlow(_lamps[i].Material, colour, false);
        }

        var deckSize = new Vector3(DeckLength, DeckThickness, LaneWidth);
        _entryDeck = Deck("EntryDeck", new Vector3(DeckCentreX, Surface - DeckThickness / 2.0f, Lane), deckSize);
        _exitDeck = Deck("ExitDeck", new Vector3(DeckCentreX, Surface - DeckThickness / 2.0f, -Lane), deckSize);
        AddChild(Shapes.StaticCollider("Stop", new Vector3(0.04f, 0.1f, LaneWidth), new Vector3(StopX, Surface + 0.05f, Lane), friction: 0.1f));

        _exitArea = new Area3D
        {
            Name = "ExitArea",
            CollisionLayer = 0,
            CollisionMask = Layers.Boxes,
            Monitoring = true,
            Monitorable = false,
            Position = new Vector3(DeckCentreX, Surface + 0.2f, -Lane),
        };
        _exitArea.AddChild(Shapes.BoxCollider(new Vector3(DeckLength, 0.36f, LaneWidth)));
        AddChild(_exitArea);
        ShowPose();
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 along = GlobalBasis.X.Normalized();
        _entryDeck.ConstantLinearVelocity = along * DeckSpeed;
        _exitDeck.ConstantLinearVelocity = -along * DeckSpeed;

        if (_part is not null && (!IsInstanceValid(_part) || _part.IsQueuedForDeletion()))
        {
            _part = null;       // removed from under us, e.g. by a scene reset
            _cycle.Abort();
        }

        Box? atEntry = BoxAtEntry();
        EntryPresent = atEntry is not null && atEntry != _part;
        ExitOccupied = false;
        foreach (Node3D body in _exitArea.GetOverlappingBodies())
        {
            ExitOccupied |= body != _part && body is Box box && !box.IsQueuedForDeletion();
        }

        bool wasBusy = _cycle.Busy;
        _cycle.Tick(delta, Start, EntryPresent, !ExitOccupied);
        if (!wasBusy && _cycle.Busy)
        {
            _makeLid = Lid;
            _partHeight = atEntry?.Height ?? _partHeight;
        }

        bool holding = _cycle.Holding;
        if (holding && !_wasHolding && _part is null)
        {
            if (atEntry is null)
            {
                _cycle.Abort();     // the part was taken away while the arm was on its way
                holding = false;
            }
            else
            {
                Take(atEntry);
            }
        }
        else if (!holding && _wasHolding && _part is not null && _cycle.Step != MachiningStep.LoadUp)
        {
            Release();              // set down at the exit; at the machine the part stays clamped
        }

        if (_cycle.Machined && !_wasMachined && _part is not null)
        {
            Machine();
        }

        _wasHolding = holding;
        _wasMachined = _cycle.Machined;

        _shownHeight = Mathf.MoveToward(_shownHeight, _partHeight, (float)delta * 0.3f);
        if (_part is not null && holding)
        {
            _partOffset = _partOffset.MoveToward(Vector3.Zero, (float)delta * CentringSpeed);
            _part.GlobalTransform = new Transform3D(
                _partBasis, GlobalTransform * (TipLocal() - new Vector3(0.0f, _part.Height / 2.0f, 0.0f)) + _partOffset);
            _part.LinearVelocity = Vector3.Zero;
            _part.AngularVelocity = Vector3.Zero;
        }

        ShowPose();
        if (_cycle.Spindle > 0.0)
        {
            _spindle.RotateY((float)delta * 40.0f);
        }
    }

    public override void _Process(double delta)
    {
        SetLamp(0, !_cycle.Busy);
        SetLamp(1, _cycle.Busy && !_cycle.WaitingForExit);
        SetLamp(2, _cycle.WaitingForExit);
    }

    public void Reset()
    {
        if (_part is not null && IsInstanceValid(_part))
        {
            Release();
        }

        _part = null;
        _cycle.Reset();
        Start = false;
        Lid = false;
        Finished = 0;
        _wasHolding = false;
        _wasMachined = false;
        _partHeight = 0.15f;
        _shownHeight = 0.15f;
        EntryPresent = false;
        ExitOccupied = false;
        if (IsInsideTree())
        {
            ShowPose();
        }
    }

    private StaticBody3D Deck(string name, Vector3 position, Vector3 size)
    {
        StaticBody3D deck = Shapes.StaticCollider(name, size, position, friction: 1.0f);
        AddChild(deck);
        return deck;
    }

    /// <summary>Underside of the gripper pad in local space.</summary>
    private Vector3 TipLocal()
    {
        float angle = Mathf.DegToRad((float)_cycle.ArmAngle);
        float height = Surface + _shownHeight + (1.0f - (float)_cycle.Lowered) * Lift;
        return new Vector3(BaseX + Reach * Mathf.Cos(angle), height, -Reach * Mathf.Sin(angle));
    }

    private void ShowPose()
    {
        Vector3 tip = TipLocal();
        _arm.Position = _armRest + new Vector3(0.0f, tip.Y - TipHeight, 0.0f);
        _arm.Rotation = new Vector3(0.0f, Mathf.DegToRad((float)_cycle.ArmAngle), 0.0f);
        float stroke = SpindleHeight - (Surface + _shownHeight);
        _spindle.Position = _spindleRest - new Vector3(0.0f, (float)_cycle.Spindle * stroke, 0.0f);
    }

    /// <summary>The part lying against the stop, if any: a line across the deck just before the blade.</summary>
    private Box? BoxAtEntry()
    {
        float x = StopX - 0.02f - 0.06f;
        float y = Surface + 0.03f;
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalTransform * new Vector3(x, y, Lane - LaneWidth / 2.0f),
            GlobalTransform * new Vector3(x, y, Lane + LaneWidth / 2.0f), Layers.Boxes);
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<GodotObject>() as Box : null;
    }

    private void Take(Box box)
    {
        _part = box;
        _partHeight = box.Height;
        _shownHeight = box.Height;
        _partBasis = box.GlobalBasis;
        Vector3 under = GlobalTransform * (TipLocal() - new Vector3(0.0f, box.Height / 2.0f, 0.0f));
        _partOffset = box.GlobalPosition - under;
        _partOffset.Y = 0.0f;
        box.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        box.Freeze = true;
    }

    private void Release()
    {
        if (_part is null)
        {
            return;
        }

        _part.Freeze = false;
        _part.LinearVelocity = Vector3.Zero;
        _part.AngularVelocity = Vector3.Zero;
        _part = null;
        if (_cycle.Step == MachiningStep.DropUp)
        {
            Finished++;
        }
    }

    /// <summary>Replaces the clamped part by what the machine makes of it.</summary>
    private void Machine()
    {
        string? product = MachiningCycle.Product(_part!.ItemModel, _makeLid);
        if (product is null || Models.Catalog?.Find(product) is not { } asset || BoxParent is null)
        {
            return;
        }

        var made = new Box
        {
            ItemModel = asset.Id,
            ItemSize = new Vector3((float)asset.Size[0], (float)asset.Size[1], (float)asset.Size[2]),
            ItemMass = _part.ItemMass,
            SpawnTime = _part.SpawnTime,
            FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
            Freeze = true,
        };
        BoxParent.AddChild(made);
        Vector3 position = _part.GlobalPosition;
        position.Y += (made.Height - _part.Height) / 2.0f;      // stands on the same table
        made.GlobalTransform = new Transform3D(_partBasis, position);
        _part.QueueFree();
        _part = made;
        _partHeight = made.Height;
    }

    private void SetLamp(int index, bool on)
    {
        if (_lampOn[index] != on)
        {
            _lampOn[index] = on;
            Models.SetGlow(_lamps[index].Material, _lamps[index].Colour, on);
        }
    }
}
