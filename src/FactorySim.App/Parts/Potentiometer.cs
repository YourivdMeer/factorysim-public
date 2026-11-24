using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A potentiometer: a knob the operator turns, one Int sensor tag from 0 to 1000. A click on the
/// left half of the knob turns it a step down, on the right half a step up; the mouse wheel over the
/// knob does the same. The node origin is the centre of the mounting face; the knob faces local +Z.
/// </summary>
public partial class Potentiometer : Node3D
{
    private readonly KnobLogic _logic;
    private Node3D? _knob;
    private Transform3D _rest;

    public Potentiometer()
        : this(0)
    {
    }

    public Potentiometer(int start)
    {
        _logic = new KnobLogic(start);
    }

    /// <summary>Name of the model in the library; it must have a Knob part.</summary>
    public string ModelName { get; set; } = "potentiometer";

    /// <summary>Value of the sensor tag, 0 to 1000.</summary>
    public int Value => _logic.Value;

    /// <summary>Where a click counts: the centre of the knob.</summary>
    public Vector3 PickPoint => _knob?.GlobalPosition ?? GlobalPosition;

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _knob = Models.Part(model, "Knob");
        _rest = _knob.Transform;
        Apply();
    }

    /// <summary>Turns the knob by a number of steps; positive is to the right (higher).</summary>
    public void Step(int steps)
    {
        _logic.Step(steps);
        Apply();
    }

    public void Set(int value)
    {
        _logic.Set(value);
        Apply();
    }

    public void Reset()
    {
        _logic.Reset();
        Apply();
    }

    private void Apply()
    {
        if (_knob is not null)
        {
            // Clockwise seen from the front (+Z) is a negative turn about +Z.
            _knob.Transform = _rest.RotatedLocal(Vector3.Back, -Mathf.DegToRad((float)_logic.AngleDegrees));
        }
    }
}
