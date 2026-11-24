using System.Collections.Generic;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Signal lamp, warning light or stack light: one or more lamps, each switched by its own
/// actuator tag. The lamps are the model parts called "Lamp" or "Lamp_&lt;colour&gt;"; each
/// lights up in the colour of its own material.
/// </summary>
public partial class IndicatorLamp : Node3D
{
    private readonly List<(StandardMaterial3D Material, Color Colour)> _lamps = new();
    private bool[] _on = System.Array.Empty<bool>();

    /// <summary>Name of the model in the library.</summary>
    public string ModelName { get; set; } = "light_indicator_green";

    /// <summary>Names of the lamp parts, in the order of the tags.</summary>
    public IReadOnlyList<string> LampParts { get; set; } = new[] { "Lamp" };

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _on = new bool[LampParts.Count];
        foreach (string part in LampParts)
        {
            MeshInstance3D mesh = Models.Part(model, part);
            Color colour = Models.LampColour(mesh.Mesh.SurfaceGetMaterial(0)) ?? Colors.White;
            StandardMaterial3D material = Models.OwnMaterial(mesh);
            Models.SetGlow(material, colour, on: false);
            _lamps.Add((material, colour));
        }
    }

    public bool IsOn(int lamp) => _on[lamp];

    public void Set(int lamp, bool on)
    {
        if (lamp >= _on.Length || _on[lamp] == on)
        {
            return;
        }

        _on[lamp] = on;
        Models.SetGlow(_lamps[lamp].Material, _lamps[lamp].Colour, on);
    }

    public void Reset()
    {
        for (int i = 0; i < _on.Length; i++)
        {
            Set(i, false);
        }
    }
}
