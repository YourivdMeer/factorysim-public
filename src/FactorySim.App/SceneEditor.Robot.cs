using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.App.Ui;
using FactorySim.Core.Scenes;
using FactorySim.Core.Robot;
using Godot;

namespace FactorySim.App;

/// <summary>
/// The editor's panel for teaching positions to a robot arm. The panel edits a draft pose: the
/// arm in the scene jumps to it, by axis or by a point the editor solves for. <i>Nieuw</i> and
/// <i>Overschrijf</i> store the draft as a numbered position of the item. Every change of the
/// positions is one step for undo. The logic is in <see cref="RobotPoses"/> and
/// <see cref="RobotKinematics"/>; this file only shows it.
/// </summary>
public partial class SceneEditor
{
    private static readonly string[] AxisNames = { "As 1", "As 2", "As 3", "As 4", "As 5", "As 6" };

    private readonly SpinBox[] _robotAxes = new SpinBox[RobotAngles.Count];
    private RobotAngles _robotDraft = RobotAngles.Home;
    private string? _robotDraftFor;
    private int _robotPose;
    private Label? _robotTip;

    /// <summary>The pose the panel is editing; the arm in the scene stands on it.</summary>
    public RobotAngles RobotDraft => _robotDraft;

    /// <summary>The position selected in the list; 0 is home.</summary>
    public int SelectedRobotPose => _robotPose;

    /// <summary>The taught positions of the selected robot, or none.</summary>
    public IReadOnlyList<RobotPose> SelectedRobotPoses =>
        _selectedId is not null && _main.Scene.Find(_selectedId) is { } item ? item.Poses ?? Array.Empty<RobotPose>() : Array.Empty<RobotPose>();

    /// <summary>Puts the draft (and the arm) on these angles, clamped to the axis ranges.</summary>
    public void SetRobotDraft(RobotAngles angles)
    {
        if (SelectedRobot() is not { } robot)
        {
            return;
        }

        _robotDraft = robot.Geometry.Clamp(angles);
        _robotDraftFor = robot.Item.Id;
        ShowDraft(robot);
    }

    /// <summary>
    /// Solves the angles for a point relative to the foot of the robot and makes them the draft.
    /// False, with a message, when the point is out of reach; the draft then stays as it was.
    /// </summary>
    public bool SetRobotDraftToPoint(Vector3d point, bool toolDown)
    {
        if (SelectedRobot() is not { } robot)
        {
            return false;
        }

        RobotAngles? angles = RobotKinematics.Solve(robot.Geometry, point, toolDown, _robotDraft);
        if (angles is null)
        {
            ShowStatus($"Punt {point} ligt buiten het bereik van de robot.", isError: true);
            return false;
        }

        SetRobotDraft(angles.Value);
        return true;
    }

    /// <summary>Selects a position in the list and puts the arm on it ("Ga naar"); 0 is home.</summary>
    public void SelectRobotPose(int number)
    {
        if (SelectedRobot() is not { } robot)
        {
            return;
        }

        RobotPose? pose = robot.Item.Poses?.FirstOrDefault(candidate => candidate.Number == number);
        if (number != 0 && pose is null)
        {
            return;
        }

        _robotPose = number;
        SetRobotDraft(pose?.Angles ?? RobotAngles.Home);
        UpdateSelectionUi();
    }

    /// <summary>Stores the draft as a new position under the lowest free number; null or an error message.</summary>
    public string? AddRobotPose(string name)
    {
        string? error = ChangePoses(poses => RobotPoses.Add(poses, name, _robotDraft), out IReadOnlyList<RobotPose>? after);
        if (error is null && after is not null)
        {
            _robotPose = after.Where(pose => pose.Name == name.Trim() && pose.Angles == _robotDraft).Select(pose => pose.Number).DefaultIfEmpty(0).Max();
            UpdateSelectionUi();
        }

        return error;
    }

    public string? OverwriteRobotPose() =>
        _robotPose == 0 ? Refuse("Home kan niet worden gewijzigd.") : ChangePoses(poses => RobotPoses.Overwrite(poses, _robotPose, _robotDraft), out _);

    public string? RemoveRobotPose()
    {
        if (_robotPose == 0)
        {
            return Refuse("Home kan niet worden verwijderd.");
        }

        string? error = ChangePoses(poses => RobotPoses.Remove(poses, _robotPose), out _);
        if (error is null)
        {
            _robotPose = 0;
            UpdateSelectionUi();
        }

        return error;
    }

    public string? RenameRobotPose(string name) =>
        _robotPose == 0 ? Refuse("Home heeft een vaste naam.") : ChangePoses(poses => RobotPoses.Rename(poses, _robotPose, name), out _);

    public string? RenumberRobotPose(int number)
    {
        if (_robotPose == 0 || number == _robotPose)
        {
            return _robotPose == 0 ? Refuse("Home is altijd positie 0.") : null;
        }

        string? error = ChangePoses(poses => RobotPoses.Renumber(poses, _robotPose, number), out _);
        if (error is null)
        {
            _robotPose = number;
            UpdateSelectionUi();
        }

        return error;
    }

    private string Refuse(string message)
    {
        ShowStatus(message, isError: true);
        return message;
    }

    private string? ChangePoses(Func<IReadOnlyList<RobotPose>?, IReadOnlyList<RobotPose>> change, out IReadOnlyList<RobotPose>? after)
    {
        after = null;
        if (SelectedRobot() is not { } robot)
        {
            return "Geen robot geselecteerd.";
        }

        try
        {
            after = change(robot.Item.Poses);
        }
        catch (SceneException e)
        {
            return Refuse(e.Message);
        }

        _history.Record(_main.Scene);
        _main.Scene.SetPoses(robot.Item.Id, after);
        return Rebuild();
    }

    private (SceneItem Item, RobotGeometry Geometry)? SelectedRobot()
    {
        if (_selectedId is null || _main.Scene.Find(_selectedId) is not { } item
            || _main.Catalog.Find(item.Asset) is not { Behaviour: AssetBehaviours.Robot } asset)
        {
            return null;
        }

        return (item, RobotGeometry.FromParams((name, fallback) => item.Param(name, asset, fallback)));
    }

    /// <summary>Jumps the arm of the selected robot to the draft and shows the draft in the fields.</summary>
    private void ShowDraft((SceneItem Item, RobotGeometry Geometry) robot)
    {
        if (_main.View.NodeOf(robot.Item.Id) is RobotArm arm)
        {
            arm.Jump(_robotDraft);
        }

        bool updating = _updatingSelectionUi;
        _updatingSelectionUi = true;
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            if (_robotAxes[axis] is { } box && IsInstanceValid(box))
            {
                box.Value = _robotDraft[axis];
            }
        }

        _updatingSelectionUi = updating;
        if (_robotTip is not null && IsInstanceValid(_robotTip))
        {
            Vector3d tip = RobotKinematics.Forward(robot.Geometry, _robotDraft).Tip;
            _robotTip.Text = string.Format(CultureInfo.CurrentCulture, "Grijperpunt: X {0:0.000}  Y {1:0.000}  Z {2:0.000} m", tip.X, tip.Y, tip.Z);
        }
    }

    /// <summary>The section *Posities* of the properties panel, for a robot arm.</summary>
    private void FillRobotPanel(SceneItem item, RobotGeometry geometry)
    {
        if (_robotDraftFor != item.Id)
        {
            _robotDraftFor = item.Id;
            _robotDraft = RobotAngles.Home;
            _robotPose = 0;
        }

        if (_robotPose != 0 && item.Poses?.All(pose => pose.Number != _robotPose) != false)
        {
            _robotPose = 0;
        }

        _paramBox.AddChild(new HSeparator());
        _paramBox.AddChild(Shell.Heading("Posities"));
        var list = new ItemList { CustomMinimumSize = new Vector2(0.0f, 130.0f), TooltipText = "Klik op een positie: de arm gaat erheen" };
        var numbers = new List<int> { 0 };
        list.AddItem("0 · Home");
        foreach (RobotPose pose in item.Poses ?? Array.Empty<RobotPose>())
        {
            numbers.Add(pose.Number);
            list.AddItem($"{pose.Number} · {pose.Name}");
        }

        list.Select(Math.Max(0, numbers.IndexOf(_robotPose)));
        list.ItemSelected += index => SelectRobotPose(numbers[(int)index]);
        _paramBox.AddChild(list);

        RobotPose? selected = item.Poses?.FirstOrDefault(pose => pose.Number == _robotPose);
        var nameRow = new HBoxContainer();
        var name = new LineEdit
        {
            Text = selected?.Name ?? "Home",
            Editable = selected is not null,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Naam van de positie; Enter past toe. Bij Nieuw wordt dit de naam van de nieuwe positie.",
        };
        name.TextSubmitted += text => RenameRobotPose(text);
        nameRow.AddChild(name);
        var number = new SpinBox
        {
            MinValue = 1,
            MaxValue = RobotController.MaxPosition,
            Step = 1,
            Value = Math.Max(1, _robotPose),
            Editable = selected is not null,
            TooltipText = "Nummer voor de PLC (bits pos 1, 2, 4, 8)",
        };
        number.ValueChanged += value =>
        {
            if (!_updatingSelectionUi)
            {
                RenumberRobotPose((int)value);
            }
        };
        nameRow.AddChild(number);
        _paramBox.AddChild(nameRow);

        var buttons = new HBoxContainer();
        buttons.AddChild(Shell.MakeButton("Nieuw", () => AddRobotPose(NewPoseName(item, name.Text)), "Bewaar de stand van de arm als nieuwe positie"));
        buttons.AddChild(Shell.MakeButton("Overschrijf", () => OverwriteRobotPose(), "Bewaar de stand van de arm in de gekozen positie"));
        buttons.AddChild(Shell.MakeButton("Verwijder", () => RemoveRobotPose(), "Verwijder de gekozen positie; de andere houden hun nummer"));
        buttons.AddChild(Shell.MakeButton("Ga naar", () => SelectRobotPose(_robotPose), "Zet de arm op de gekozen positie"));
        _paramBox.AddChild(buttons);

        _paramBox.AddChild(Shell.Heading("Assen (°)"));
        var axes = new GridContainer { Columns = 4 };
        axes.AddThemeConstantOverride("h_separation", 8);
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            int index = axis;
            axes.AddChild(new Label { Text = AxisNames[axis] });
            var box = new SpinBox
            {
                MinValue = geometry.Min[axis],
                MaxValue = geometry.Max[axis],
                Step = 0.5,
                Value = _robotDraft[axis],
                CustomMinimumSize = new Vector2(90.0f, 0.0f),
                TooltipText = $"{geometry.Min[axis]:0} tot {geometry.Max[axis]:0}°",
            };
            box.ValueChanged += value =>
            {
                if (!_updatingSelectionUi)
                {
                    double[] values = _robotDraft.ToArray();
                    values[index] = value;
                    SetRobotDraft(RobotAngles.FromArray(values));
                }
            };
            _robotAxes[axis] = box;
            axes.AddChild(box);
        }

        _paramBox.AddChild(axes);

        _paramBox.AddChild(Shell.Heading("Op punt (m, vanaf de voet)"));
        Vector3d tip = RobotKinematics.Forward(geometry, _robotDraft).Tip;
        var point = new GridContainer { Columns = 6 };
        point.AddThemeConstantOverride("h_separation", 6);
        SpinBox x = AddPointField(point, "X", tip.X);
        SpinBox y = AddPointField(point, "Y", tip.Y);
        SpinBox z = AddPointField(point, "Z", tip.Z);
        _paramBox.AddChild(point);
        var solveRow = new HBoxContainer();
        var down = new CheckBox { Text = "Grijper omlaag", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        solveRow.AddChild(down);
        solveRow.AddChild(Shell.MakeButton("Reken uit", () => SetRobotDraftToPoint(new Vector3d(x.Value, y.Value, z.Value), down.ButtonPressed),
            "Zoek de ashoeken waarmee de grijperpunt op dit punt komt"));
        _paramBox.AddChild(solveRow);
        _robotTip = new Label { Modulate = Shell.Muted };
        _paramBox.AddChild(_robotTip);
        _paramBox.AddChild(new Label
        {
            Text = "De arm gaat van positie naar positie in een boog (PTP) en botst nergens tegen. Leer een tussenpositie boven een rand.",
            Modulate = Shell.Muted,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        ShowDraft((item, geometry));
    }

    private static SpinBox AddPointField(Container parent, string label, double value)
    {
        parent.AddChild(new Label { Text = label });
        var box = new SpinBox { MinValue = -1.0, MaxValue = 1.5, Step = 0.005, Value = value, CustomMinimumSize = new Vector2(80.0f, 0.0f) };
        parent.AddChild(box);
        return box;
    }

    private static string NewPoseName(SceneItem item, string typed)
    {
        bool fresh = typed.Trim().Length > 0 && typed.Trim() != "Home" && item.Poses?.All(pose => pose.Name != typed.Trim()) != false;
        return fresh ? typed : $"Positie {(item.Poses?.Count ?? 0) + 1}";
    }
}
