using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// Push button, emergency stop or selector: the operator clicks it and the PLC reads one
/// sensor tag. The node origin is the centre of the mounting face; the control faces local +Z.
/// What the contact does is decided by <see cref="ButtonLogic"/>; this node only shows it.
/// </summary>
public partial class OperatorControl : Node3D
{
    /// <summary>How far a button travels when pushed in, in metres.</summary>
    private const float PressDepth = 0.008f;

    /// <summary>How far a selector knob turns when switched over, in degrees.</summary>
    private const float KnobAngle = 60.0f;

    /// <summary>Seconds a door takes to swing open or shut.</summary>
    private const float SwingSeconds = 0.5f;

    private readonly ButtonLogic _logic;
    private float _swing;
    private Node3D? _moving;
    private Transform3D _rest;
    private StandardMaterial3D? _lit;
    private Color _litColour;

    public OperatorControl()
        : this(latching: false, normallyClosed: false)
    {
    }

    public OperatorControl(bool latching, bool normallyClosed)
    {
        _logic = new ButtonLogic(latching, normallyClosed);
    }

    /// <summary>Name of the model in the library; it must have a Button or a Knob part.</summary>
    public string ModelName { get; set; } = "push_button_green";

    /// <summary>True for a control that turns (selector) instead of being pushed in.</summary>
    public bool Turns { get; set; }

    /// <summary>True for a door: the part called Leaf swings open about the vertical axis while the control is engaged.</summary>
    public bool Swings { get; set; }

    /// <summary>How far a door swings open, in degrees.</summary>
    public float SwingAngle { get; set; } = 90.0f;

    /// <summary>Where a click counts, seen from the node origin: for a door the middle of the leaf, not its foot.</summary>
    public Vector3 PickOffset { get; set; }

    public Vector3 PickPoint => ToGlobal(PickOffset);

    /// <summary>How far a door stands open at this moment, in degrees.</summary>
    public float Swing => _swing;

    public bool Latching => _logic.Latching;

    /// <summary>True while the control is pushed in or switched over.</summary>
    public bool Engaged => _logic.Engaged;

    /// <summary>Value of the sensor tag.</summary>
    public bool Signal => _logic.Signal;

    public override void _Ready()
    {
        if (Swings)
        {
            ProcessMode = ProcessModeEnum.Always;        // a door also swings while the simulation is paused
        }

        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _moving = Models.Part(model, Swings ? "Leaf" : Turns ? "Knob" : "Button");
        _rest = _moving.Transform;

        // An illuminated button lights up while it is pushed in.
        var mesh = (MeshInstance3D)_moving;
        if (Models.LampColour(mesh.Mesh.SurfaceGetMaterial(0)) is { } colour)
        {
            _lit = Models.OwnMaterial(mesh);
            _litColour = colour;
        }

        Apply();
    }

    public void Press()
    {
        _logic.Press();
        Apply();
    }

    public void Release()
    {
        _logic.Release();
        Apply();
    }

    public void Reset()
    {
        _logic.Reset();
        _swing = 0.0f;
        Apply();
    }

    public override void _Process(double delta)
    {
        if (!Swings)
        {
            return;
        }

        float target = Engaged ? SwingAngle : 0.0f;
        if (!Mathf.IsEqualApprox(_swing, target))
        {
            _swing = Mathf.MoveToward(_swing, target, SwingAngle / SwingSeconds * (float)delta);
            Apply();
        }
    }

    // Applied at once instead of animated: the control must also respond while the simulation is stopped.
    private void Apply()
    {
        if (_moving is null)
        {
            return;
        }

        if (Swings)
        {
            _moving.Transform = _rest.RotatedLocal(Vector3.Up, -Mathf.DegToRad(_swing));
        }
        else if (Turns)
        {
            _moving.Transform = _rest.RotatedLocal(Vector3.Back, Engaged ? Mathf.DegToRad(-KnobAngle) : 0.0f);
        }
        else
        {
            _moving.Transform = _rest.TranslatedLocal(new Vector3(0.0f, 0.0f, Engaged ? -PressDepth : 0.0f));
        }

        if (_lit is not null)
        {
            _lit.EmissionEnabled = true;
            _lit.Emission = _litColour;
            _lit.EmissionEnergyMultiplier = Engaged ? 1.5f : 0.0f;
        }
    }
}
