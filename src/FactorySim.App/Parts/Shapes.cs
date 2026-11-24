using Godot;

namespace FactorySim.App.Parts;

/// <summary>Small helpers for building primitive geometry in code.</summary>
public static class Shapes
{
    public static StandardMaterial3D Material(Color color, float roughness = 0.8f)
    {
        var material = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness };
        if (color.A < 1.0f)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        return material;
    }

    public static StandardMaterial3D Unshaded(Color color)
    {
        StandardMaterial3D material = Material(color);
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return material;
    }

    public static MeshInstance3D BoxMesh(Vector3 size, Material material, Vector3 position = default)
    {
        return new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
        };
    }

    public static CollisionShape3D BoxCollider(Vector3 size, Vector3 position = default)
    {
        return new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
            Position = position,
        };
    }

    /// <summary>A fixed, invisible box collider on the world layer; a model supplies the looks.</summary>
    public static StaticBody3D StaticCollider(string name, Vector3 size, Vector3 position, float friction = 0.6f)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = friction },
        };
        body.AddChild(BoxCollider(size));
        return body;
    }

    /// <summary>A fixed box with a collider on the world layer.</summary>
    public static StaticBody3D StaticBox(string name, Vector3 size, Vector3 position, Color color, float friction = 0.6f)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = friction },
        };
        body.AddChild(BoxMesh(size, Material(color)));
        body.AddChild(BoxCollider(size));
        return body;
    }
}
