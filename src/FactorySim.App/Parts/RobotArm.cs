using FactorySim.Core.Robot;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A six-axis robot arm on a pedestal. The PLC picks a taught position and gives a go; the
/// <see cref="RobotController"/> moves the arm there point to point. Every tick the seven parts of
/// the model are put where <see cref="RobotKinematics.Forward"/> says, the same function the unit
/// tests check. The suction cup takes hold of the item it touches while grip is on; the item then
/// follows the flange, turns included, and falls free when grip drops.
///
/// The node origin is on the floor under the foot; the foot stands on the pedestal. The arm has no
/// collider: it passes through belts and boxes. The item it holds does collide.
/// </summary>
public partial class RobotArm : Node3D
{
    /// <summary>How far beyond the suction cup an item counts as touched, in metres.</summary>
    private const float TouchRange = 0.02f;

    private readonly MeshInstance3D[] _links = new MeshInstance3D[RobotKinematics.LinkCount];
    private MeshInstance3D? _pedestal;
    private Box? _held;
    private Transform3D _heldFromTool;

    public RobotArm()
        : this(RobotGeometry.Default, 40.0, 0.75f)
    {
    }

    public RobotArm(RobotGeometry geometry, double speedPercent, float pedestal)
    {
        Controller = new RobotController(geometry, speedPercent);
        Pedestal = Mathf.Max(0.0f, pedestal);
    }

    public RobotController Controller { get; }

    /// <summary>Name of the model in the library; it must have Base, Link_1 to Link_6 and Pedestal.</summary>
    public string ModelName { get; set; } = "robot_6axis";

    /// <summary>Height of the pedestal the model was built with, in metres; the pedestal is stretched from it.</summary>
    public float ModelPedestal { get; set; } = 0.75f;

    /// <summary>Height of the foot above the floor, in metres.</summary>
    public float Pedestal { get; }

    /// <summary>True while the suction cup touches or holds an item.</summary>
    public bool ItemDetected { get; private set; }

    public bool Holding => _held is not null;

    /// <summary>The tip of the suction cup in world space.</summary>
    public Vector3 TipPosition => ToolTransform(RobotKinematics.Forward(Controller.Geometry, Controller.Angles)).Origin;

    /// <summary>A point given relative to the foot of the robot (as the editor and the tests use them) in world space.</summary>
    public Vector3 FootToWorld(Vector3d point) => GlobalTransform * FootLocal(point);

    /// <summary>A world point relative to the foot of the robot.</summary>
    public Vector3d WorldToFoot(Vector3 point)
    {
        Vector3 local = GlobalTransform.AffineInverse() * point;
        return new Vector3d(local.X, local.Y - Pedestal, local.Z);
    }

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        for (int i = 0; i < RobotKinematics.LinkCount; i++)
        {
            _links[i] = Models.Part(model, RobotKinematics.PartNames[i]);
        }

        _pedestal = Models.Part(model, "Pedestal");
        PlaceParts();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_held is not null && (!IsInstanceValid(_held) || _held.IsQueuedForDeletion()))
        {
            _held = null;       // removed from under us, e.g. by a remover or a scene reset
        }

        Controller.Tick(delta);
        RobotFrames frames = PlaceParts();
        Transform3D tool = ToolTransform(frames);

        Box? touched = _held is null ? BoxAtTip(tool) : null;
        if (Controller.Grip && _held is null && touched is not null)
        {
            Take(touched, tool);
        }
        else if (!Controller.Grip && _held is not null)
        {
            Release();
        }

        if (_held is not null)
        {
            _held.GlobalTransform = tool * _heldFromTool;
            _held.LinearVelocity = Vector3.Zero;
            _held.AngularVelocity = Vector3.Zero;
        }

        ItemDetected = _held is not null || touched is not null;
    }

    /// <summary>Puts the arm on a pose at once, for the editor's "go to".</summary>
    public void Jump(RobotAngles angles)
    {
        Controller.Jump(angles);
        if (IsInsideTree())
        {
            PlaceParts();
        }
    }

    public void Reset()
    {
        if (_held is not null && IsInstanceValid(_held))
        {
            Release();
        }

        _held = null;
        ItemDetected = false;
        Controller.Reset();
        if (IsInsideTree())
        {
            PlaceParts();
        }
    }

    private Vector3 FootLocal(Vector3d point) => new((float)point.X, (float)point.Y + Pedestal, (float)point.Z);

    private static Basis ToBasis(Matrix3d m) => new(
        new Vector3((float)m.M00, (float)m.M10, (float)m.M20),
        new Vector3((float)m.M01, (float)m.M11, (float)m.M21),
        new Vector3((float)m.M02, (float)m.M12, (float)m.M22));

    private RobotFrames PlaceParts()
    {
        RobotFrames frames = RobotKinematics.Forward(Controller.Geometry, Controller.Angles);
        Transform3D global = GlobalTransform;
        for (int i = 0; i < RobotKinematics.LinkCount; i++)
        {
            LinkFrame link = frames.Links[i];
            _links[i].GlobalTransform = global * new Transform3D(ToBasis(link.Rotation), FootLocal(link.Origin));
        }

        if (_pedestal is not null)
        {
            _pedestal.Visible = Pedestal > 0.001f;
            float stretch = ModelPedestal > 0.0f ? Pedestal / ModelPedestal : 1.0f;
            _pedestal.GlobalTransform = global * new Transform3D(Basis.Identity.Scaled(new Vector3(1.0f, Mathf.Max(stretch, 0.001f), 1.0f)), Vector3.Zero);
        }

        return frames;
    }

    /// <summary>The flange in world space, moved to the tip of the suction cup.</summary>
    private Transform3D ToolTransform(RobotFrames frames) =>
        GlobalTransform * new Transform3D(ToBasis(frames.ToolRotation), FootLocal(frames.Tip));

    private Box? BoxAtTip(Transform3D tool)
    {
        // At home the tool points down (-Y); the turned tool axis is the turned -Y.
        Vector3 direction = -tool.Basis.Y.Normalized();
        Vector3 tip = tool.Origin;
        var query = PhysicsRayQueryParameters3D.Create(tip - direction * 0.01f, tip + direction * TouchRange, Layers.Boxes);
        query.HitFromInside = true;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].As<GodotObject>() as Box : null;
    }

    private void Take(Box box, Transform3D tool)
    {
        _held = box;
        _heldFromTool = tool.AffineInverse() * box.GlobalTransform;
        box.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
        box.Freeze = true;
    }

    private void Release()
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
    }
}
