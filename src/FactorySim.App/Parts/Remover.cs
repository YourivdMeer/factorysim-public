using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>Removes every box that enters its volume and counts them.</summary>
public partial class Remover : Area3D
{
    public Vector3 Size { get; set; } = new(0.6f, 1.2f, 1.0f);

    /// <summary>Name of the model in res://models, without extension.</summary>
    public string ModelName { get; set; } = "remover_hood_1m0";

    /// <summary>Rotation of the model about the vertical axis, to point its entrance at the belt.</summary>
    public float ModelYawDegrees { get; set; }

    public int Count => LowCount + HighCount + OtherCount;

    /// <summary>Removed items that are not one of the two boxes, e.g. pallets.</summary>
    public int OtherCount { get; private set; }

    public int LowCount { get; private set; }

    public int HighCount { get; private set; }

    /// <summary>Raised with every item it removes, once, before the item is freed.</summary>
    public event System.Action<Box>? ItemRemoved;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.Boxes;
        Monitoring = true;
        Monitorable = false;

        AddChild(Shapes.BoxCollider(Size));

        // Discharge cabinet; its origin is the centre of this volume and its entrance faces local -X.
        Node3D model = Models.Instantiate(ModelName);
        model.RotationDegrees = new Vector3(0.0f, ModelYawDegrees, 0.0f);
        AddChild(model);

        BodyEntered += OnBodyEntered;
    }

    public void Reset()
    {
        LowCount = 0;
        HighCount = 0;
        OtherCount = 0;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is not Box box || box.IsQueuedForDeletion())
        {
            return;
        }

        if (!box.IsStandardBox)
        {
            OtherCount++;
        }
        else if (box.Kind == BoxKind.High)
        {
            HighCount++;
        }
        else
        {
            LowCount++;
        }

        ItemRemoved?.Invoke(box);
        box.QueueFree();
    }
}
