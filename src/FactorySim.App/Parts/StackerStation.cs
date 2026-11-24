using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Stacker crane: a cart on a rail along local X, a platform that hoists along the masts, forks
/// that reach to both sides along local Z and a lift that raises a pallet off its support. The
/// node origin is on the floor in the middle of the aisle. All motion comes from
/// <see cref="Logic"/>; this part moves the model, carries a pallet and senses the load.
///
/// The forks have a collider, the rest of the model has none. A pallet that lies on the forks
/// when the lift is raised is frozen and carried along (like the gripper of pick and place) and
/// let go when the lift is lowered again with the forks reaching out over a support.
/// </summary>
public partial class StackerStation : Node3D
{
    /// <summary>Lift height from which a pallet on the forks is taken along, in metres.</summary>
    private const float TakeLift = 0.05f;

    /// <summary>Lift height at or below which a carried pallet is set down, in metres.</summary>
    private const float ReleaseLift = 0.035f;

    /// <summary>The forks must reach out at least this far before a pallet is set down, in metres.</summary>
    private const float ReleaseReach = 0.5f;

    private Node3D _cart = null!;
    private Node3D _platform = null!;
    private Node3D _forks = null!;
    private AnimatableBody3D _forksBody = null!;

    private Box? _held;
    private Vector3 _heldOffset;
    private Basis _heldBasis;

    public StackerStation(StackerPositions positions, bool jog, double driveSpeed, double hoistSpeed,
        double forkStroke, double forkSpeed, double liftStroke, double liftSpeed)
    {
        Logic = new FactorySim.Core.StackerCrane(positions, jog, driveSpeed, hoistSpeed, forkStroke, forkSpeed, liftStroke, liftSpeed);
    }

    public FactorySim.Core.StackerCrane Logic { get; }

    /// <summary>Name of the model in the library; it must have Cart, Platform and Forks parts.</summary>
    public string ModelName { get; set; } = "stacker_crane";

    public float ForkLength { get; set; } = 1.5f;

    public float ForkSpacing { get; set; } = 0.5f;

    public float ForkWidth { get; set; } = 0.1f;

    public float ForkThickness { get; set; } = 0.04f;

    /// <summary>Something lies on the forks.</summary>
    public bool Load { get; private set; }

    public bool Holding => _held is not null;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _cart = Models.Part(model, "Cart");
        _platform = Models.Part(model, "Platform");
        _forks = Models.Part(model, "Forks");

        _forksBody = new AnimatableBody3D
        {
            Name = "ForksBody",
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            SyncToPhysics = true,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f },
        };
        foreach (float side in new[] { -0.5f, 0.5f })
        {
            _forksBody.AddChild(Shapes.BoxCollider(
                new Vector3(ForkWidth, ForkThickness, ForkLength),
                new Vector3(side * ForkSpacing, -ForkThickness / 2.0f, 0.0f)));
        }

        AddChild(_forksBody);
        MoveParts();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_held is not null && (!IsInstanceValid(_held) || _held.IsQueuedForDeletion()))
        {
            _held = null;       // removed from under us, e.g. by a scene reset
        }

        Logic.Tick(delta);
        MoveParts();

        Box? below = BoxOnForks();
        Load = _held is not null || below is not null;
        if (_held is null && below is not null && Logic.LiftOffset >= TakeLift)
        {
            Take(below);
        }
        else if (_held is not null && Logic.LiftOffset <= ReleaseLift && Mathf.Abs((float)Logic.ForkOffset) >= ReleaseReach)
        {
            Release();
        }

        if (_held is not null)
        {
            _held.GlobalTransform = new Transform3D(_heldBasis, _forks.GlobalPosition + _heldOffset);
            _held.LinearVelocity = Vector3.Zero;
            _held.AngularVelocity = Vector3.Zero;
        }
    }

    public void Reset()
    {
        if (_held is not null && IsInstanceValid(_held))
        {
            Release();
        }

        _held = null;
        Load = false;
        Logic.Reset();
        if (IsInsideTree())
        {
            MoveParts();
        }
    }

    private void MoveParts()
    {
        var forks = new Vector3(
            (float)Logic.X, (float)(Logic.PlatformHeight + Logic.LiftOffset), (float)Logic.ForkOffset);
        _cart.Position = new Vector3((float)Logic.X, 0.0f, 0.0f);
        _platform.Position = new Vector3((float)Logic.X, (float)Logic.PlatformHeight, 0.0f);
        _forks.Position = forks;
        _forksBody.Position = forks;
    }

    /// <summary>The item that lies directly on the top face of the forks, if any.</summary>
    private Box? BoxOnForks()
    {
        Vector3 top = _forks.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(top + Vector3.Up * 0.02f, top + Vector3.Down * 0.02f, Layers.Boxes);
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<GodotObject>() as Box : null;
    }

    private void Take(Box box)
    {
        _held = box;
        _heldOffset = box.GlobalPosition - _forks.GlobalPosition;
        _heldBasis = box.GlobalBasis;
        box.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        box.Freeze = true;
    }

    private void Release()
    {
        if (_held is null)
        {
            return;
        }

        _held.Freeze = false;
        _held.LinearVelocity = Vector3.Zero;
        _held.AngularVelocity = Vector3.Zero;
        _held = null;
    }
}
