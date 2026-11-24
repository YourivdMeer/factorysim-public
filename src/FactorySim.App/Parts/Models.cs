using System;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Loads the visual models in res://models (built by assets/blender_scripts/build_assets.py).
/// Models are visual only: colliders stay the primitives defined in the parts.
/// </summary>
public static class Models
{
    /// <summary>The asset library; set once at start-up. Maps asset ids to model files.</summary>
    public static AssetCatalog? Catalog { get; set; }

    /// <summary>Instantiates the model of a library asset (or res://models/&lt;name&gt;.glb without a catalogue).</summary>
    public static Node3D Instantiate(string name)
    {
        string path = Catalog?.Find(name) is { } asset ? "res://" + asset.File : $"res://models/{name}.glb";
        var scene = GD.Load<PackedScene>(path)
            ?? throw new InvalidOperationException($"Model {path} is missing. Run the asset generator and re-import.");
        Node3D model = scene.Instantiate<Node3D>();
        model.Name = "Model";
        return model;
    }

    /// <summary>Finds a named part of a model, such as "Belt" or "SensorLed".</summary>
    public static MeshInstance3D Part(Node model, string partName)
    {
        return model.FindChild(partName, recursive: true, owned: false) as MeshInstance3D
            ?? throw new InvalidOperationException($"Model '{model.Name}' has no part named '{partName}'.");
    }

    /// <summary>
    /// Gives a mesh its own copy of a surface material, so changing it (scrolling a belt,
    /// lighting an LED) does not affect other instances of the same model.
    /// </summary>
    public static StandardMaterial3D OwnMaterial(MeshInstance3D mesh, int surface = 0)
    {
        var material = (StandardMaterial3D)mesh.Mesh.SurfaceGetMaterial(surface).Duplicate();
        mesh.SetSurfaceOverrideMaterial(surface, material);
        return material;
    }

    /// <summary>
    /// Colour a lamp material lights up in, from its name in the generator
    /// (FS_LampGreen, FS_LampRed, ...). Null when the material is not a lamp.
    /// </summary>
    public static Color? LampColour(Material? material)
    {
        string name = material?.ResourceName ?? string.Empty;
        const string prefix = "FS_Lamp";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return name[prefix.Length..] switch
        {
            "Green" => new Color(0.15f, 0.95f, 0.25f),
            "Red" => new Color(1.0f, 0.16f, 0.10f),
            "Yellow" => new Color(1.0f, 0.80f, 0.10f),
            "Blue" => new Color(0.20f, 0.45f, 1.0f),
            _ => new Color(0.95f, 0.95f, 0.90f),
        };
    }

    /// <summary>Switches a lamp or LED material on or off.</summary>
    public static void SetGlow(StandardMaterial3D material, Color colour, bool on)
    {
        material.EmissionEnabled = true;
        material.Emission = colour;
        material.EmissionEnergyMultiplier = on ? 2.5f : 0.0f;
        material.AlbedoColor = on ? colour : colour.Darkened(0.8f);
    }
}
