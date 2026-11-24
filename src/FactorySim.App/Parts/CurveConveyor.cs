using System.Collections.Generic;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// 90-degree conveyor bend. The node origin is the centre of the bend at transport height.
/// Seen from above the deck runs from local +X round to local -Z; boxes enter at +X moving
/// towards -Z and leave at -Z moving towards -X.
///
/// Like the straight conveyor nothing really moves. The deck is a fan of static bodies, each
/// reporting a surface velocity along its own tangent; a box crossing from one to the next is
/// steered round the bend. (One body with an angular surface velocity would be simpler, but the
/// physics engine does not carry boxes with it: they stood still on the deck.)
/// </summary>
public partial class CurveConveyor : Node3D
{
    private const float BodyHeight = 0.2f;
    private const int Segments = 12;

    private readonly List<(StaticBody3D Body, Vector3 LocalTangent)> _segments = new();
    private bool _running;

    /// <summary>Radius of the centre line of the deck, in metres. Must match the model.</summary>
    public float Radius { get; set; } = 1.0f;

    /// <summary>Deck width, in metres. Must match the model.</summary>
    public float Width { get; set; } = 0.8f;

    /// <summary>Surface speed, in metres per second.</summary>
    public float Speed { get; set; } = 0.6f;

    /// <summary>Name of the model in the library.</summary>
    public string ModelName { get; set; } = "belt_conveyor_curved";

    public bool Running
    {
        get => _running;
        set
        {
            _running = value;
            ApplySurfaceVelocity();
        }
    }

    public override void _Ready()
    {
        // Each slice is a box as wide as the deck and as long as the outer edge of the slice,
        // so neighbours overlap a little and leave no gap.
        float slice = Mathf.Pi / 2.0f / Segments;
        float outer = Radius + Width / 2.0f;
        float length = 2.0f * outer * Mathf.Sin(slice / 2.0f) + 0.01f;
        for (int i = 0; i < Segments; i++)
        {
            float angle = slice * (i + 0.5f);
            var body = new StaticBody3D
            {
                Name = $"Segment{i}",
                CollisionLayer = Layers.World,
                CollisionMask = 0,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1.0f },
                Position = new Vector3(Radius * Mathf.Cos(angle), -BodyHeight / 2.0f, -Radius * Mathf.Sin(angle)),
                Rotation = new Vector3(0.0f, angle, 0.0f),      // local X points outwards
            };
            body.AddChild(Shapes.BoxCollider(new Vector3(Width, BodyHeight, length)));
            AddChild(body);

            // Direction of travel at this angle: the derivative of (cos a, 0, -sin a).
            _segments.Add((body, new Vector3(-Mathf.Sin(angle), 0.0f, -Mathf.Cos(angle))));
        }

        AddChild(Models.Instantiate(ModelName));
        ApplySurfaceVelocity();
    }

    private void ApplySurfaceVelocity()
    {
        if (!IsInsideTree())
        {
            return;
        }

        foreach ((StaticBody3D body, Vector3 localTangent) in _segments)
        {
            body.ConstantLinearVelocity = _running ? GlobalBasis * localTangent * Speed : Vector3.Zero;
        }
    }
}
