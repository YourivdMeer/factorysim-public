using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>A cardboard box in one of two heights.</summary>
public partial class Box : RigidBody3D
{
    public const string Group = "boxes";
    public const float Footprint = 0.30f;
    public const float LowHeight = 0.20f;
    public const float HighHeight = 0.35f;

    public BoxKind Kind { get; set; } = BoxKind.Low;

    /// <summary>Set for an item from the library other than the two boxes, e.g. "pallet".</summary>
    public string? ItemModel { get; set; }

    /// <summary>Size of that library item, in metres.</summary>
    public Vector3 ItemSize { get; set; }

    public float ItemMass { get; set; } = 2.0f;

    /// <summary>Simulation time at which an emitter made the item; NaN when unknown (plan 11).</summary>
    public double SpawnTime { get; set; } = double.NaN;

    /// <summary>Set once the item has been counted as fallen, so it is counted only once.</summary>
    public bool FallenCounted { get; set; }

    public bool IsStandardBox => ItemModel is null;

    public Vector3 Size => ItemModel is null ? new Vector3(Footprint, HeightOf(Kind), Footprint) : ItemSize;

    public float Height => Size.Y;

    public static float HeightOf(BoxKind kind) => kind == BoxKind.High ? HighHeight : LowHeight;

    public override void _Ready()
    {
        AddToGroup(Group);
        CollisionLayer = Layers.Boxes;
        CollisionMask = Layers.World | Layers.Boxes;
        Mass = ItemMass;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.7f, Bounce = 0.0f };
        AngularDamp = 2.0f;
        CanSleep = false;

        AddChild(Shapes.BoxCollider(Size));
        AddChild(Models.Instantiate(ItemModel ?? (Kind == BoxKind.High ? "box_high" : "box_low")));
    }
}
