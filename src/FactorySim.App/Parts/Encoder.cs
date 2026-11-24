using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Incremental encoder: a wheel that rides on the belt below it and gives two tracks of pulses (A
/// and B) while the belt moves. The node origin is the centre of the wheel; the wheel rolls along
/// local +X, so stand the encoder with the same turn as the belt. The surface speed is read from
/// the belt under the wheel; <see cref="EncoderLogic"/> makes the pulses.
/// </summary>
public partial class Encoder : Node3D
{
    private EncoderLogic? _logic;
    private RayCast3D? _ray;
    private MeshInstance3D? _wheel;

    /// <summary>Name of the model in the library; it must have a Wheel part.</summary>
    public string ModelName { get; set; } = "incremental_encoder";

    /// <summary>Distance that makes one period of A and B, in metres.</summary>
    public float PulseDistance { get; set; } = 0.05f;

    public float WheelRadius { get; set; } = 0.04f;

    /// <summary>Built on first use, so the tags can be wired before the node enters the tree.</summary>
    private EncoderLogic Logic => _logic ??= new EncoderLogic(PulseDistance);

    public bool A => Logic.A;

    public bool B => Logic.B;

    /// <summary>Distance travelled since the last reset, in metres.</summary>
    public double Distance => Logic.Distance;

    public override void _Ready()
    {
        // Looks down for the surface the wheel stands on.
        _ray = new RayCast3D
        {
            TargetPosition = new Vector3(0.0f, -(WheelRadius + 0.06f), 0.0f),
            CollisionMask = Layers.World,
            Enabled = true,
            HitFromInside = true,
        };
        AddChild(_ray);

        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _wheel = Models.Part(model, "Wheel");
    }

    public override void _PhysicsProcess(double delta)
    {
        double distance = 0.0;
        if (_ray is { } ray && ray.IsColliding() && ray.GetCollider() is StaticBody3D surface)
        {
            // A driven surface reports its speed in world coordinates; take the part along our rolling direction.
            distance = surface.ConstantLinearVelocity.Dot(GlobalBasis.X.Normalized()) * delta;
        }

        Logic.Advance(distance);
        _wheel?.RotateZ((float)(distance / WheelRadius));
    }

    public void Reset() => Logic.Reset();
}
