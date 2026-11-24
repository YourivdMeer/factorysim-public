using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Storage rack: three slots wide and four levels high. A pallet rests on two beams per slot
/// that run in the depth of the rack; everything else is visual. The node origin is on the
/// floor in the middle of the rack.
/// </summary>
public partial class RackStation : Node3D
{
    /// <summary>Name of the model in the library.</summary>
    public string ModelName { get; set; } = "rack";

    public float ColumnSpacing { get; set; } = 1.5f;

    public float LevelHeight { get; set; } = 1.2f;

    /// <summary>Height of the top of the supports of the lowest level, in metres.</summary>
    public float FirstLevelTop { get; set; } = 0.6f;

    public float BeamSpacing { get; set; } = 0.68f;

    public float BeamWidth { get; set; } = 0.05f;

    public float BeamHeight { get; set; } = 0.1f;

    /// <summary>Length of a beam, which is the depth of the rack, in metres.</summary>
    public float Depth { get; set; } = 1.3f;

    public override void _Ready()
    {
        AddChild(Models.Instantiate(ModelName));

        var beams = new StaticBody3D
        {
            Name = "Beams",
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.7f },
        };
        for (int level = 0; level < StackerPositions.Levels; level++)
        {
            float top = FirstLevelTop + level * LevelHeight;
            for (int column = 0; column < StackerPositions.Columns; column++)
            {
                float centre = (column - (StackerPositions.Columns - 1) / 2.0f) * ColumnSpacing;
                foreach (float side in new[] { -0.5f, 0.5f })
                {
                    beams.AddChild(Shapes.BoxCollider(
                        new Vector3(BeamWidth, BeamHeight, Depth),
                        new Vector3(centre + side * BeamSpacing, top - BeamHeight / 2.0f, 0.0f)));
                }
            }
        }

        AddChild(beams);
    }
}
