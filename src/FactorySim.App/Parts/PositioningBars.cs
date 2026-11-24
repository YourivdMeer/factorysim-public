using System.Collections.Generic;
using System.Linq;
using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Positioning bars: two clamps either side of a conveyor that close together on the item between
/// them and put it on the centre line. The node origin is on the belt surface in the middle of the
/// clamps; the clamps close along local Z. Timing and the stop on an item are in
/// <see cref="PositioningClamp"/>; this part measures the item and moves the blades.
/// </summary>
public partial class PositioningBars : Node3D
{
    private readonly List<Blade> _blades = new();
    private PositioningClamp? _logic;

    /// <summary>Name of the model in the library; its clamps are called Clamp_Left and Clamp_Right.</summary>
    public string ModelName { get; set; } = "positioning_bars";

    /// <summary>Distance of the inner face of a blade from the centre line when open, in metres.</summary>
    public float OpenHalfGap { get; set; } = 0.52f;

    /// <summary>How far each blade travels towards the centre, in metres.</summary>
    public float Stroke { get; set; } = 0.42f;

    public float Speed { get; set; } = 0.3f;

    /// <summary>Length of a blade along the belt, and its collider's height and thickness, in metres.</summary>
    public float BladeLength { get; set; } = 0.5f;

    public float BladeHeight { get; set; } = 0.16f;

    public float BladeThickness { get; set; } = 0.04f;

    /// <summary>Height of the centre of a blade above the belt surface, in metres.</summary>
    public float BladeCentre { get; set; } = 0.12f;

    /// <summary>Built on first use, so the tags can be wired before the node enters the tree.</summary>
    private PositioningClamp Logic => _logic ??= new PositioningClamp(OpenHalfGap, Stroke, Speed);

    /// <summary>Command: true closes the clamps, false opens them.</summary>
    public bool Close
    {
        get => Logic.Close;
        set => Logic.Close = value;
    }

    public bool IsOpen => Logic.Open;

    public bool IsClosed => Logic.Closed;

    /// <summary>Distance of each blade's inner face from the centre line, in metres.</summary>
    public float HalfGap => (float)Logic.HalfGap;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);

        foreach (string name in new[] { "Clamp_Left", "Clamp_Right" })
        {
            if (model.FindChild(name, recursive: true, owned: false) is not Node3D clamp)
            {
                continue;
            }

            // The blade body rides with the model part of the same name: the part becomes its child.
            var body = new AnimatableBody3D
            {
                Name = name + "_Body",
                SyncToPhysics = true,
                CollisionLayer = Layers.World,
                CollisionMask = 0,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.3f },
                Position = clamp.Position,
            };
            AddChild(body);
            clamp.Reparent(body, keepGlobalTransform: false);
            clamp.Position = Vector3.Zero;

            // Which way is the centre line? Towards zero on the Z axis. The collider is as thick as
            // BladeThickness and its inner face lies at OpenHalfGap, so its centre is half a
            // thickness further out than that; seen from the body origin that is a few millimetres
            // towards the centre.
            float towardsCentre = body.Position.Z > 0.0f ? -1.0f : 1.0f;
            float offset = Mathf.Abs(body.Position.Z) - OpenHalfGap - BladeThickness / 2.0f;
            body.AddChild(Shapes.BoxCollider(new Vector3(BladeLength, BladeHeight, BladeThickness),
                new Vector3(0.0f, BladeCentre - body.Position.Y, towardsCentre * offset)));
            _blades.Add(new Blade(body, body.Position, towardsCentre));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Logic.Tick(delta, WidestItemHalfWidth());
        foreach (Blade blade in _blades)
        {
            blade.Body.Position = blade.Rest + new Vector3(0.0f, 0.0f, blade.TowardsCentre * (float)Logic.Position);
        }
    }

    public void Reset()
    {
        Logic.Reset();
        foreach (Blade blade in _blades)
        {
            blade.Body.Position = blade.Rest;
        }
    }

    /// <summary>
    /// Half the extent across the clamps of the widest item that stands between the blades, or null
    /// when there is none. An item counts when its centre is within the blades' length and between
    /// the open blades, from the belt surface up to the blade height and a little above.
    /// </summary>
    private double? WidestItemHalfWidth()
    {
        double? widest = null;
        foreach (Box box in GetTree().GetNodesInGroup(Box.Group).OfType<Box>())
        {
            if (box.IsQueuedForDeletion())
            {
                continue;
            }

            Vector3 local = ToLocal(box.GlobalPosition);
            if (Mathf.Abs(local.X) > BladeLength / 2.0f || Mathf.Abs(local.Z) > OpenHalfGap
                || local.Y < 0.0f || local.Y > BladeCentre + BladeHeight)
            {
                continue;
            }

            // Our Z axis in the box's own frame; the box's frame is orthonormal, so the transpose inverts it.
            Vector3 across = box.GlobalBasis.Inverse() * GlobalBasis.Z.Normalized();
            double half = PositioningClamp.HalfExtent(box.Size.X, box.Size.Y, box.Size.Z, across.X, across.Y, across.Z);
            widest = widest is null ? half : System.Math.Max(widest.Value, half);
        }

        return widest;
    }

    private sealed record Blade(AnimatableBody3D Body, Vector3 Rest, float TowardsCentre);
}
