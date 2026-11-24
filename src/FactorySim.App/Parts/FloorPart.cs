using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// The floor of the scene: a grey slab whose top face is the floor surface (y = 0), centred on the
/// origin, with a yellow safety line round the working area. It is made in code at the size the scene
/// asks for, so it can be any size. Boxes and items stand on it.
/// </summary>
public partial class FloorPart : StaticBody3D
{
    /// <summary>Width of the safety line, in metres.</summary>
    private const float LineWidth = 0.08f;

    public FloorPart()
    {
        Name = "Floor";
        CollisionLayer = Layers.World;
        CollisionMask = 0;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f };
    }

    /// <summary>Length along X and width along Z, in metres.</summary>
    public (float Length, float Width) Size { get; private set; }

    /// <summary>Builds the floor at this size; does nothing when it already has it.</summary>
    public void Resize(float length, float width)
    {
        if (Size == (length, width) && GetChildCount() > 0)
        {
            return;
        }

        Size = (length, width);
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        var slab = new Vector3(length, 0.1f, width);
        AddChild(Shapes.BoxCollider(slab, new Vector3(0.0f, -0.05f, 0.0f)));
        AddChild(Shapes.BoxMesh(slab, Shapes.Material(new Color(0.42f, 0.43f, 0.45f), 0.95f), new Vector3(0.0f, -0.05f, 0.0f)));

        // The safety line runs round the working area, a metre in from the edge on a normal floor.
        float inset = Mathf.Min(1.0f, Mathf.Min(length, width) / 6.0f);
        float lineLength = length - 2.0f * inset;
        float lineWidth = width - 2.0f * inset;
        StandardMaterial3D yellow = Shapes.Material(new Color(0.96f, 0.74f, 0.05f), 0.6f);
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            AddChild(Shapes.BoxMesh(new Vector3(lineLength + LineWidth, 0.004f, LineWidth), yellow, new Vector3(0.0f, 0.002f, side * lineWidth / 2.0f)));
            AddChild(Shapes.BoxMesh(new Vector3(LineWidth, 0.004f, lineWidth), yellow, new Vector3(side * lineLength / 2.0f, 0.002f, 0.0f)));
        }
    }
}
