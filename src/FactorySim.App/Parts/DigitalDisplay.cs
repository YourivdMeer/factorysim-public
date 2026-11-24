using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A digital display: shows the number of one Int actuator tag on its screen, with a fixed number
/// of decimals. The text is a Label3D just in front of the model part called Screen, which faces local +Z.
/// </summary>
public partial class DigitalDisplay : Node3D
{
    private Label3D? _label;

    /// <summary>Name of the model in the library; it must have a Screen part.</summary>
    public string ModelName { get; set; } = "digital_display";

    /// <summary>Decimals shown: 2 shows 1234 as 12,34.</summary>
    public int Decimals { get; set; }

    /// <summary>The number the PLC wrote last.</summary>
    public int Number { get; private set; }

    /// <summary>What the screen shows.</summary>
    public string Text => _label?.Text ?? string.Empty;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        MeshInstance3D screen = Models.Part(model, "Screen");
        Aabb box = screen.GetAabb();
        Vector3 centre = box.GetCenter();
        _label = new Label3D
        {
            Name = "Digits",
            PixelSize = 0.0008f,
            FontSize = 64,
            OutlineSize = 0,
            Modulate = new Color(0.55f, 1.0f, 0.55f),
            HorizontalAlignment = HorizontalAlignment.Center,
            // Just in front of the screen face; a Label3D faces +Z like the screen.
            Position = screen.Position + new Vector3(centre.X, centre.Y, box.End.Z + 0.002f),
        };
        AddChild(_label);
        SetNumber(Number);
    }

    public void SetNumber(int number)
    {
        Number = number;
        if (_label is not null)
        {
            _label.Text = DisplayText.Format(number, Decimals);
        }
    }

    public void Reset() => SetNumber(0);
}
