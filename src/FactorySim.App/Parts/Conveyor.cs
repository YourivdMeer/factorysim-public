using System.Collections.Generic;
using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Belt conveyor. The belt itself does not move: the static body reports a surface
/// velocity, and friction carries the boxes along. Travel direction is local +X.
/// The model is visual only; while running, its belt texture scrolls and its end rollers turn.
/// </summary>
public partial class Conveyor : StaticBody3D
{
    private const float BodyHeight = 0.2f;

    /// <summary>Metres of belt per texture repeat; set by the asset generator.</summary>
    private const float StripePeriod = 0.25f;

    /// <summary>Radius of the end rollers of a belt conveyor model.</summary>
    private const float RollerRadius = 0.06f;

    /// <summary>Radius of the rollers of a roller conveyor model.</summary>
    private const float BedRollerRadius = 0.046f;

    private readonly List<MeshInstance3D> _rollers = new();
    private StandardMaterial3D? _beltMaterial;
    private float _rollerRadius = RollerRadius;
    private float _uvOffset;
    private bool _running;
    private bool _backward;

    /// <summary>Belt length along local X, in metres. Must match the model.</summary>
    public float Length { get; set; } = 2.0f;

    /// <summary>Belt width along local Z, in metres. Must match the model.</summary>
    public float Width { get; set; } = 0.8f;

    /// <summary>
    /// Height gained over <see cref="Length"/>, in metres; 0 is a flat belt. The belt then climbs
    /// towards local +X and <see cref="Length"/> is the horizontal length.
    /// </summary>
    public float Rise { get; set; }

    /// <summary>Run the other way: downhill on a belt that climbs. A fixed setting, not a tag.</summary>
    public bool Reverse { get; set; }

    /// <summary>
    /// Two-way belt (plan 12): run against the forward direction, which is the one <see cref="Reverse"/> sets.
    /// Set from the tag "&lt;name&gt; reverse" through <see cref="ConveyorDrive"/>.
    /// </summary>
    public bool Backward
    {
        get => _backward;
        set
        {
            _backward = value;
            ApplySurfaceVelocity();
        }
    }

    /// <summary>Two-way belt: forward and reverse are both on, so it stands still.</summary>
    public bool Conflict { get; set; }

    /// <summary>The direction it actually runs: <see cref="Reverse"/> turned round once more by <see cref="Backward"/>.</summary>
    private bool RunsReversed => Reverse != _backward;

    /// <summary>Surface speed in metres per second.</summary>
    public float Speed { get; set; } = 0.6f;

    /// <summary>Name of the model in res://models, without extension.</summary>
    public string ModelName { get; set; } = "conveyor_straight_6m";

    public bool Running
    {
        get => _running;
        set
        {
            _running = value;
            ApplySurfaceVelocity();
        }
    }

    /// <summary>True for a weighing conveyor: it reports the mass of the items resting on it (<see cref="WeightKg"/>).</summary>
    public bool Weighs { get; set; }

    /// <summary>
    /// Weighing conveyor: total mass of the items whose body is in a flat volume just above the belt, in kg.
    /// A volume instead of contact forces: steady, and the same headless and with a window.
    /// </summary>
    public double WeightKg
    {
        get
        {
            double total = 0.0;
            if (_scale is not null)
            {
                foreach (Node3D body in _scale.GetOverlappingBodies())
                {
                    if (body is Box box && !box.Freeze)
                    {
                        total += box.Mass;
                    }
                }
            }

            return total;
        }
    }

    private Area3D? _scale;

    /// <summary>Height of the belt surface above the node origin.</summary>
    public static float SurfaceOffset => BodyHeight / 2.0f;

    public override void _Ready()
    {
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f };

        // A belt that climbs gets a tilted box whose top face runs along the surface and passes
        // through the point under the model origin.
        var slope = new ConveyorSlope(Length, Rise);
        (double offsetX, double offsetY) = slope.ColliderOffset(BodyHeight);
        CollisionShape3D collider = Shapes.BoxCollider(
            new Vector3((float)slope.SurfaceLength, BodyHeight, Width), new Vector3((float)offsetX, (float)offsetY, 0.0f));
        collider.Rotation = new Vector3(0.0f, 0.0f, (float)slope.Angle);
        AddChild(collider);

        if (Weighs)
        {
            // A thin layer just above the belt: an item resting on it reaches into the layer, one passing over does not.
            _scale = new Area3D { Name = "Scale", CollisionLayer = 0, CollisionMask = Layers.Boxes, Monitorable = false };
            _scale.AddChild(Shapes.BoxCollider(new Vector3(Length, 0.04f, Width), new Vector3(0.0f, SurfaceOffset + 0.025f, 0.0f)));
            AddChild(_scale);
        }

        // The model's origin is the centre of the belt top; the node origin is the collider centre.
        Node3D model = Models.Instantiate(ModelName);
        model.Position = new Vector3(0.0f, SurfaceOffset, 0.0f);
        AddChild(model);

        // A belt conveyor has a Belt and two end rollers; a roller conveyor has a bed of
        // rollers and no belt. Whatever is called Roller_* turns with the surface speed.
        if (model.FindChild("Belt", recursive: true, owned: false) is MeshInstance3D belt)
        {
            _beltMaterial = Models.OwnMaterial(belt);
        }
        else
        {
            _rollerRadius = BedRollerRadius;
        }

        foreach (Node child in model.FindChildren("Roller_*", "MeshInstance3D", recursive: true, owned: false))
        {
            _rollers.Add((MeshInstance3D)child);
        }

        ApplySurfaceVelocity();
    }

    public override void _Process(double delta)
    {
        if (!_running)
        {
            return;
        }

        float speed = RunsReversed ? -Speed : Speed;
        if (_beltMaterial is not null)
        {
            // Shift the texture against the UV direction so the stripes travel with the boxes.
            _uvOffset = Mathf.PosMod(_uvOffset - speed / StripePeriod * (float)delta, 1.0f);
            _beltMaterial.Uv1Offset = new Vector3(_uvOffset, 0.0f, 0.0f);
        }

        float angle = -speed / _rollerRadius * (float)delta;
        foreach (MeshInstance3D roller in _rollers)
        {
            roller.RotateZ(angle);
        }
    }

    private void ApplySurfaceVelocity()
    {
        if (!IsInsideTree())
        {
            return;
        }

        // Along the surface, in world coordinates: the local X axis tilted up by the slope.
        (double along, double up) = new ConveyorSlope(Length, Rise).Direction(RunsReversed);
        Vector3 direction = GlobalBasis.X.Normalized() * (float)along + GlobalBasis.Y.Normalized() * (float)up;
        ConstantLinearVelocity = _running ? direction * Speed : Vector3.Zero;
    }
}
