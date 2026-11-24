using System.Collections.Generic;
using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Optical sensor: one or more rays along local +Z that report whether an item is within range.
/// Only items interrupt a ray; machine parts are ignored. This one part is the diffuse sensor, the
/// capacitive and the inductive sensor (which only sees metal items), the reflection sensor (its
/// range runs to the reflector) and the receiver of a light array (a row of rays one above the
/// other). The model's LED lights up and its beam, if it has one, brightens while it detects.
/// </summary>
public partial class PhotoEye : Node3D
{
    private static readonly Color LedColour = new(1.0f, 0.16f, 0.10f);

    private readonly List<RayCast3D> _rays = new();
    private StandardMaterial3D? _ledMaterial;
    private StandardMaterial3D? _beamMaterial;

    /// <summary>Name of the model in the library; it must have a SensorLed part, and may have a SensorBeam.</summary>
    public string ModelName { get; set; } = "photo_eye";

    /// <summary>Sensing range in metres.</summary>
    public float Range { get; set; } = 1.0f;

    /// <summary>Number of rays, one above the other along local Y, centred on the origin.</summary>
    public int Beams { get; set; } = 1;

    /// <summary>Distance between two rays, in metres.</summary>
    public float BeamSpacing { get; set; } = 0.05f;

    /// <summary>True for an inductive sensor: it ignores everything that is not metal.</summary>
    public bool MetalOnly { get; set; }

    /// <summary>False for a sensor that needs a partner and has none: it never detects anything.</summary>
    public bool Active { get; set; } = true;

    public bool Detecting { get; private set; }

    public override void _Ready()
    {
        for (int i = 0; i < Beams; i++)
        {
            var ray = new RayCast3D
            {
                Position = new Vector3(0.0f, (i - (Beams - 1) / 2.0f) * BeamSpacing, 0.0f),
                TargetPosition = new Vector3(0.0f, 0.0f, Range),
                CollisionMask = Layers.Boxes,
                Enabled = Active,
                HitFromInside = true,
            };
            AddChild(ray);
            _rays.Add(ray);
        }

        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _ledMaterial = Models.OwnMaterial(Models.Part(model, "SensorLed"));

        // The beam, where the model has one, is modelled 1 m long from the lens; stretch it to the sensing range.
        if (model.FindChild("SensorBeam", recursive: true, owned: false) is MeshInstance3D beam)
        {
            beam.Scale = new Vector3(1.0f, 1.0f, Range);
            _beamMaterial = Models.OwnMaterial(beam);
        }

        ShowState(false);
    }

    /// <summary>Number of beams that an item interrupts (a light array has several).</summary>
    public int InterruptedBeams { get; private set; }

    /// <summary>Library id of the item the first interrupted beam hits, or null: what a vision sensor sees.</summary>
    public string? SeenItem { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        int interrupted = 0;
        string? seen = null;
        foreach (RayCast3D ray in _rays)
        {
            if (ray.IsColliding() && ray.GetCollider() is var collider && Qualifies(collider))
            {
                interrupted++;
                var box = (Box)collider!;
                seen ??= box.ItemModel ?? (box.Kind == BoxKind.High ? "box_high" : "box_low");
            }
        }

        InterruptedBeams = interrupted;
        SeenItem = seen;
        bool detecting = interrupted > 0;

        if (detecting == Detecting)
        {
            return;
        }

        Detecting = detecting;
        ShowState(detecting);
    }

    private bool Qualifies(GodotObject? collider) => collider is Box box && (!MetalOnly || ItemKinds.IsMetal(box.ItemModel));

    private void ShowState(bool detecting)
    {
        if (_ledMaterial is not null)
        {
            Models.SetGlow(_ledMaterial, LedColour, detecting);
        }

        if (_beamMaterial is not null)
        {
            Color colour = _beamMaterial.AlbedoColor;
            colour.A = detecting ? 0.85f : 0.3f;
            _beamMaterial.AlbedoColor = colour;
        }
    }
}
