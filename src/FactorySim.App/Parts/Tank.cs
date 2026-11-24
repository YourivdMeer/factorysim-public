using FactorySim.Core;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A tank with a fill valve and a discharge valve that the PLC sets from 0 to 100 %, and a level
/// and a flow that it reads back. The liquid is simulated by <see cref="TankLogic"/>; the model part
/// called Level, a column in the sight glass with its origin at the bottom, is stretched in height.
/// The node origin is on the floor at the centre of the tank.
/// </summary>
public partial class Tank : Node3D
{
    private TankLogic _logic = new(3.0, 2.0, 0.2, 0.2);
    private Node3D? _column;
    private Transform3D _columnRest;

    /// <summary>Name of the model in the library; it must have a Level part.</summary>
    public string ModelName { get; set; } = "tank";

    /// <summary>Height of a full tank, in m.</summary>
    public double TankHeight { get; set; } = 3.0;

    public double Diameter { get; set; } = 2.0;

    /// <summary>Inflow with the fill valve fully open, in m³/s.</summary>
    public double MaxInflow { get; set; } = 0.2;

    /// <summary>Outflow of a full tank with the discharge valve fully open, in m³/s.</summary>
    public double MaxOutflow { get; set; } = 0.2;

    /// <summary>Height of the column in the sight glass of a full tank, in m (the part is 1 m at scale 1).</summary>
    public float ColumnHeight { get; set; } = 2.6f;

    /// <summary>Fill valve as the PLC sets it, 0 to 100 %.</summary>
    public int FillValve { get; set; }

    /// <summary>Discharge valve as the PLC sets it, 0 to 100 %.</summary>
    public int DischargeValve { get; set; }

    /// <summary>Height of the liquid, in m.</summary>
    public double Level => _logic.Level;

    /// <summary>Discharge flow, in l/s.</summary>
    public double FlowLitres => _logic.Outflow * 1000.0;

    public bool Overflowing => _logic.Overflowing;

    public override void _Ready()
    {
        _logic = new TankLogic(TankHeight, Diameter, MaxInflow, MaxOutflow);
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        _column = Models.Part(model, "Level");
        _columnRest = _column.Transform;

        // The vessel is in the way of items: a cylinder as tall as the tank body.
        var body = new StaticBody3D { Name = "Vessel", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            // The body stands on legs 0.4 m above the floor.
            Shape = new CylinderShape3D { Radius = (float)Diameter / 2.0f + 0.03f, Height = (float)TankHeight },
            Position = new Vector3(0.0f, 0.4f + (float)TankHeight / 2.0f, 0.0f),
        });
        AddChild(body);
        ShowScene();
    }

    public override void _PhysicsProcess(double delta)
    {
        _logic.Step(delta, FillValve / 100.0, DischargeValve / 100.0);
        ShowScene();
    }

    /// <summary>Sets the level directly, for a test.</summary>
    public void SetLevel(double level)
    {
        _logic.SetLevel(level);
        ShowScene();
    }

    public void Reset()
    {
        _logic.Reset();
        FillValve = 0;
        DischargeValve = 0;
        ShowScene();
    }

    private void ShowScene()
    {
        if (_column is null)
        {
            return;
        }

        float height = Mathf.Max(0.001f, ColumnHeight * (float)_logic.Fraction);
        _column.Transform = _columnRest.ScaledLocal(new Vector3(1.0f, height, 1.0f));
    }
}
