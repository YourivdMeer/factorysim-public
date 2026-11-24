using System.Collections.Generic;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A part that only has to be in the way: a chute, free rollers, a guide or a corner stop. It has
/// no tags. The colliders are boxes from the catalogue, in the part's own frame, one fixed body per
/// friction value; the model is only for the looks.
/// </summary>
public partial class PassivePart : Node3D
{
    /// <summary>Name of the model in the library.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>The boxes to build, from <see cref="CatalogAsset.Colliders"/>.</summary>
    public IReadOnlyList<ColliderSpec> Colliders { get; set; } = new List<ColliderSpec>();

    public override void _Ready()
    {
        AddChild(Models.Instantiate(ModelName));
        AddColliders(this, Colliders);
    }

    /// <summary>Adds the boxes of a catalogue collider to a part, in the part's own frame; also used by <see cref="Sign"/>.</summary>
    public static void AddColliders(Node3D part, IReadOnlyList<ColliderSpec> colliders)
    {
        foreach (IGrouping<double, ColliderSpec> group in colliders.GroupBy(collider => collider.Friction))
        {
            var body = new StaticBody3D
            {
                Name = $"Colliders_{group.Key:0.###}",
                CollisionLayer = Layers.World,
                CollisionMask = 0,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)group.Key },
            };
            foreach (ColliderSpec spec in group)
            {
                CollisionShape3D shape = Shapes.BoxCollider(
                    new Vector3((float)spec.Size[0], (float)spec.Size[1], (float)spec.Size[2]),
                    new Vector3((float)spec.Position[0], (float)spec.Position[1], (float)spec.Position[2]));

                // A positive yaw turns +X towards -Z (about +Y); a positive pitch lowers the +X end (about -Z).
                shape.RotationDegrees = new Vector3(0.0f, (float)spec.Yaw, -(float)spec.Pitch);
                body.AddChild(shape);
            }

            part.AddChild(body);
        }
    }
}
