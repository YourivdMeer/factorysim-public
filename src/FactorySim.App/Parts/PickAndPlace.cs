using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Pick and place station: a gantry whose gripper travels along X (and Y on the three-axis
/// station), goes down along Z and holds a box by suction. The node origin is on the floor at
/// the centre of the frame. Each axis is monostable: it travels out while its tag is on and
/// back when the tag drops. The Z axis stops when the gripper (or the box it holds) touches
/// something, so it comes to rest on a box of any height and sets a box down on any surface.
///
/// The frame has no collider: it is visual only, like the other models.
/// </summary>
public partial class PickAndPlace : Node3D
{
    /// <summary>The gripper stops this far above what it touches, in metres.</summary>
    private const float TouchGap = 0.003f;

    /// <summary>How far below the suction cups a box counts as touched, in metres.</summary>
    private const float TouchRange = 0.02f;

    private readonly LinearAxis _x;
    private readonly LinearAxis? _y;
    private readonly LinearAxis _z;

    private Node3D _beam = null!;
    private Node3D _carriage = null!;
    private Node3D _picker = null!;
    private Vector3 _beamRest;
    private Vector3 _carriageRest;
    private Vector3 _pickerRest;

    private Box? _held;
    private Vector3 _heldOffset;
    private Basis _heldBasis;

    public PickAndPlace()
        : this(strokeX: 1.0f, strokeY: 0.0f, strokeZ: 0.7f, speed: 1.0f, speedZ: 0.6f)
    {
    }

    public PickAndPlace(float strokeX, float strokeY, float strokeZ, float speed, float speedZ)
    {
        _x = new LinearAxis(strokeX, speed);
        _y = strokeY > 0.0f ? new LinearAxis(strokeY, speed) : null;
        _z = new LinearAxis(strokeZ, speedZ);
    }

    /// <summary>Name of the model in the library; it must have Beam, Carriage and Picker parts.</summary>
    public string ModelName { get; set; } = "two_axis_pick_and_place";

    /// <summary>Local X of the gripper with the X axis at its start, in metres.</summary>
    public float RestX { get; set; } = -0.5f;

    /// <summary>Height of the suction cups above the node origin with the Z axis up, in metres.</summary>
    public float TipHeight { get; set; } = 1.22f;

    public bool HasY => _y is not null;

    public bool MoveX
    {
        get => _x.Extend;
        set => _x.Extend = value;
    }

    public bool MoveY
    {
        get => _y?.Extend ?? false;
        set
        {
            if (_y is not null)
            {
                _y.Extend = value;
            }
        }
    }

    public bool MoveZ
    {
        get => _z.Extend;
        set => _z.Extend = value;
    }

    /// <summary>Suction on: the gripper takes hold of the box it touches and keeps it until this drops.</summary>
    public bool Grab { get; set; }

    public bool MovingX => _x.Moving;

    public bool MovingY => _y?.Moving ?? false;

    public bool MovingZ => _z.Moving;

    /// <summary>True while the gripper touches or holds a box.</summary>
    public bool ItemDetected { get; private set; }

    public bool Holding => _held is not null;

    /// <summary>Position of the suction cups in world space.</summary>
    public Vector3 TipPosition => GlobalTransform * TipLocal();

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _beam = Models.Part(model, "Beam");
        _carriage = Models.Part(model, "Carriage");
        _picker = Models.Part(model, "Picker");
        _beamRest = _beam.Position;
        _carriageRest = _carriage.Position;
        _pickerRest = _picker.Position;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_held is not null && (!IsInstanceValid(_held) || _held.IsQueuedForDeletion()))
        {
            _held = null;       // removed from under us, e.g. by a scene reset
        }

        _x.Tick(delta);
        _y?.Tick(delta);
        _z.Tick(delta, _z.Position + ClearanceBelow());
        MoveParts();

        Box? touched = BoxBelowTip();
        if (Grab && _held is null && touched is not null)
        {
            Take(touched);
        }
        else if (!Grab && _held is not null)
        {
            Release(assemble: true);
        }

        if (_held is not null)
        {
            _held.GlobalTransform = new Transform3D(_heldBasis, TipPosition + _heldOffset);
            _held.LinearVelocity = Vector3.Zero;
            _held.AngularVelocity = Vector3.Zero;
        }

        ItemDetected = _held is not null || touched is not null;
    }

    public void Reset()
    {
        if (_held is not null && IsInstanceValid(_held))
        {
            Release(assemble: false);
        }

        _held = null;
        Grab = false;
        _x.Reset();
        _y?.Reset();
        _z.Reset();
        ItemDetected = false;
        if (IsInsideTree())
        {
            MoveParts();
        }
    }

    private Vector3 TipLocal() =>
        new(RestX + (float)_x.Position, TipHeight - (float)_z.Position, (float)(_y?.Position ?? 0.0));

    private void MoveParts()
    {
        var alongX = new Vector3((float)_x.Position, 0.0f, 0.0f);
        var alongY = new Vector3(0.0f, 0.0f, (float)(_y?.Position ?? 0.0));
        _beam.Position = _beamRest + alongX;
        _carriage.Position = _carriageRest + alongX + alongY;
        _picker.Position = _pickerRest + alongX + alongY - new Vector3(0.0f, (float)_z.Position, 0.0f);
    }

    /// <summary>Free travel below the gripper, or below the box it holds, in metres.</summary>
    private float ClearanceBelow()
    {
        Vector3 from = TipPosition;
        if (_held is not null)
        {
            // Measure from the underside of the held box, wherever it hangs under the cups.
            from = _held.GlobalPosition - new Vector3(0.0f, _held.Height / 2.0f, 0.0f);
        }

        float reach = (float)(_z.Stroke - _z.Position) + 0.05f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * reach, Layers.World | Layers.Boxes);
        if (_held is not null)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { _held.GetRid() };
        }

        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return reach;
        }

        return Mathf.Max(0.0f, from.Y - ((Vector3)hit["position"]).Y - TouchGap);
    }

    private Box? BoxBelowTip()
    {
        Vector3 tip = TipPosition;
        var query = PhysicsRayQueryParameters3D.Create(
            tip + Vector3.Up * 0.01f, tip + Vector3.Down * TouchRange, Layers.Boxes);
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<GodotObject>() as Box : null;
    }

    private void Take(Box box)
    {
        _held = box;
        _heldOffset = box.GlobalPosition - TipPosition;
        _heldBasis = box.GlobalBasis;
        box.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        box.Freeze = true;
    }

    private void Release(bool assemble)
    {
        if (_held is null)
        {
            return;
        }

        Box released = _held;
        released.Freeze = false;
        released.LinearVelocity = Vector3.Zero;
        released.AngularVelocity = Vector3.Zero;
        _held = null;
        if (assemble)
        {
            TryAssemble(released);
        }
    }

    /// <summary>
    /// A lid that is set down on a base, with the middles less than <see cref="Assembly.MaxOffset"/> apart,
    /// is joined with it into one final product (<see cref="Assembly.Result"/>): the new item takes the
    /// place of the base and the two old ones disappear. A lid anywhere else stays a lid.
    /// </summary>
    private void TryAssemble(Box lid)
    {
        if (lid.ItemModel is null || Models.Catalog is not { } catalog)
        {
            return;
        }

        // The base is what the lid rests on: look down from just inside its underside.
        Vector3 from = lid.GlobalPosition - new Vector3(0.0f, lid.Height / 2.0f - 0.002f, 0.0f);
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 0.03f, Layers.Boxes);
        query.Exclude = new Godot.Collections.Array<Rid> { lid.GetRid() };
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].As<GodotObject>() is not Box under)
        {
            return;
        }

        string? result = Assembly.Result(under.ItemModel, lid.ItemModel);
        if (result is null || catalog.Find(result) is not { } asset
            || !Assembly.Aligned(under.GlobalPosition.X, under.GlobalPosition.Z, lid.GlobalPosition.X, lid.GlobalPosition.Z))
        {
            return;
        }

        var product = new Box
        {
            ItemModel = asset.Id,
            ItemSize = new Vector3((float)asset.Size[0], (float)asset.Size[1], (float)asset.Size[2]),
            ItemMass = (float)asset.Param("mass", under.ItemMass),
            SpawnTime = under.SpawnTime,        // the final product is as old as its base
        };
        under.GetParent().AddChild(product);
        Vector3 position = under.GlobalPosition;
        position.Y += (product.Height - under.Height) / 2.0f + 0.001f;       // stands on what the base stood on
        product.GlobalTransform = new Transform3D(under.GlobalBasis, position);
        under.QueueFree();
        lid.QueueFree();
    }
}
