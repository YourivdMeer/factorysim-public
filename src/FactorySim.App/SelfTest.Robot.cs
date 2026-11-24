using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core.Scenes;
using FactorySim.Core.Robot;
using Godot;

namespace FactorySim.App;

/// <summary>
/// The "robot" self-test: a robot arm on a pedestal between two belts takes raw material from a
/// stop on the first belt and sets it down on the second, which carries it to a remover.
/// </summary>
public partial class SelfTest
{
    private const string RobotName = "Robot";
    private const int RobotItems = 4;

    private List<CraneStep>? _robotPlan;
    private int _robotIndex;
    private double _robotStepSince;
    private string? _robotId;
    private Vector3d _robotPick;
    private string _robotSavedPoses = string.Empty;

    private static string RobotTag(string suffix) => RobotName + " " + suffix;

    private static string[] RobotBits(int number) =>
        new[] { 1, 2, 4, 8 }.Where(weight => (number & weight) != 0).Select(weight => RobotTag("pos " + weight)).ToArray();

    private static readonly string[] RobotLine = { "Conveyor", "Stop" };

    private static CraneStep RobotGo(int number, params string[] more) =>
        new($"go {number}", RobotLine.Concat(RobotBits(number)).Concat(more).Append(RobotTag("go")).ToArray(),
            Array.Empty<string>(), Array.Empty<string>(), 0.2);

    private static CraneStep RobotArrive(int number, params string[] more) =>
        new($"at {number}", RobotLine.Concat(RobotBits(number)).Concat(more).ToArray(), new[] { RobotTag("at pos") },
            new[] { RobotTag("busy") }, 0.0);

    private RobotArm? Robot() => Main.View.Find<RobotArm>(RobotName);

    /// <summary>Run the line until one item rests against the stop, to see where the robot has to pick.</summary>
    private static List<CraneStep> BuildRobotProbe() => new()
    {
        new("feed one item", new[] { "Conveyor", "Stop", "Emitter" }, new[] { "Sensor" }, Array.Empty<string>(), 0.0),
        new("let it settle against the stop", RobotLine, Array.Empty<string>(), Array.Empty<string>(), 1.5),
    };

    /// <summary>The PLC program: four items from the stop on belt 1 to belt 2, then a go to a position that was not taught.</summary>
    private List<CraneStep> BuildRobotPlan()
    {
        string grip = RobotTag("grip");
        var plan = new List<CraneStep>();
        for (int item = 1; item <= RobotItems; item++)
        {
            plan.Add(new CraneStep($"item {item}: feed", new[] { "Conveyor", "Stop", "Emitter" }, new[] { "Sensor" }, Array.Empty<string>(), 0.0));
            plan.Add(new CraneStep($"item {item}: settle", RobotLine, new[] { "Sensor" }, Array.Empty<string>(), 1.0));
            plan.Add(RobotGo(1));
            plan.Add(RobotArrive(1));
            plan.Add(RobotGo(2));
            plan.Add(RobotArrive(2));
            plan.Add(new CraneStep($"item {item}: grip", RobotLine.Concat(RobotBits(2)).Append(grip).ToArray(), new[] { RobotTag("item") },
                Array.Empty<string>(), 0.2));
            plan.Add(RobotGo(1, grip));
            plan.Add(RobotArrive(1, grip));
            plan.Add(RobotGo(3, grip));
            if (item == 1)
            {
                plan.Add(new CraneStep("halfway with an item", RobotLine.Concat(RobotBits(3)).Append(grip).ToArray(), Array.Empty<string>(),
                    Array.Empty<string>(), 0.6,
                    _ =>
                    {
                        SaveScreenshot(suffix: "_robot");
                        return Robot() is { Holding: true };
                    }));
            }

            plan.Add(RobotArrive(3, grip));
            plan.Add(RobotGo(4, grip));
            plan.Add(RobotArrive(4, grip));
            plan.Add(new CraneStep($"item {item}: release", RobotLine.Concat(RobotBits(4)).ToArray(), Array.Empty<string>(), Array.Empty<string>(), 0.3,
                _ => Robot() is { Holding: false }));
            plan.Add(RobotGo(3));
            plan.Add(RobotArrive(3));
        }

        plan.Add(RobotGo(0));
        plan.Add(RobotArrive(0));
        plan.Add(new CraneStep("all items delivered", RobotLine, Array.Empty<string>(), Array.Empty<string>(), 4.0,
            _ => Main.View.Find<Remover>("Remover") is { Count: RobotItems } && Main.CountBoxes().Fallen == 0));

        // A position that was not taught: error on, the arm stays at home.
        plan.Add(new CraneStep("go 9", RobotLine.Concat(RobotBits(9)).Append(RobotTag("go")).ToArray(), Array.Empty<string>(),
            Array.Empty<string>(), 0.6,
            sensors => Sensor(sensors, RobotTag("error")) && !Sensor(sensors, RobotTag("busy")) && !Sensor(sensors, RobotTag("at pos"))
                && Robot() is { } robot && robot.Controller.Angles == RobotAngles.Home));
        return plan;
    }

    private void RunRobotPlc()
    {
        if (_robotPlan is null || !Main.Tags.SensorNames.Contains(RobotTag("busy")))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        CraneStep? step = _robotIndex < _robotPlan.Count ? _robotPlan[_robotIndex] : null;
        while (step is not null && _elapsed - _robotStepSince >= step.MinTime
            && step.WaitFor.All(name => Sensor(sensors, name)) && step.WaitNot.All(name => !Sensor(sensors, name)))
        {
            if (step.Verify is { } verify)
            {
                bool ok = verify(sensors);
                GD.Print($"SELFTEST robot plc: {step.Label}: {(ok ? "ok" : "WRONG")} at {_elapsed:0.0} s");
                if (!ok)
                {
                    RobotArm? robot = Robot();
                    GD.Print($"SELFTEST robot state: angles={robot?.Controller.Angles} holding={robot?.Holding} "
                        + $"removed={Main.View.Find<Remover>("Remover")?.Count} fallen={Main.CountBoxes().Fallen}");
                }

                SortersCheck(step.Label, ok);
            }

            _robotIndex++;
            _robotStepSince = _elapsed;
            step = _robotIndex < _robotPlan.Count ? _robotPlan[_robotIndex] : null;
        }

        var on = new HashSet<string>(step?.On ?? Array.Empty<string>());
        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>Builds the line with the editor; true when every part was placed.</summary>
    private bool BuildRobotLine()
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();
        SceneItem? robot = Editor.Place("robot_6axis", 0.0f, 0.0f);
        _robotId = robot?.Id;
        bool built = robot is not null
            // Belt 1 runs along +X at z = -0.75; the emitter puts the items on its robot side, 0.5 m from the robot.
            && Editor.Place("belt_conveyor_2m", 0.0f, -0.75f) is not null
            && Editor.Place("emitter_raw_blue", -0.75f, -0.5f) is not null
            && Editor.Place("stop_blade", 0.25f, -0.75f) is not null
            && Editor.Place("diffuse_sensor", 0.0f, -1.5f) is not null
            // Belt 2 at z = +0.75 always runs, to a remover.
            && Editor.Place("belt_conveyor_2m", 0.0f, 0.75f) is not null;
        Editor.SetSelectedParam(SceneTags.AlwaysOn, 1.0);
        built = built && Editor.Place("remover_hood_1m0", 0.75f, 0.75f) is not null;
        return built;
    }

    /// <summary>Teaches the four positions through the editor, solved from points relative to the foot.</summary>
    private bool TeachRobot()
    {
        Editor.Select(_robotId);
        var place = new Vector3d(_robotPick.X, _robotPick.Y, -_robotPick.Z);
        (string Name, Vector3d Point)[] points =
        {
            ("Boven het item", _robotPick + new Vector3d(0.0, 0.2, 0.0)),
            ("Op het item", _robotPick + new Vector3d(0.0, 0.003, 0.0)),
            ("Boven band 2", place + new Vector3d(0.0, 0.2, 0.0)),
            ("Op band 2", place + new Vector3d(0.0, 0.01, 0.0)),
        };
        bool ok = true;
        foreach ((string name, Vector3d point) in points)
        {
            bool solved = Editor.SetRobotDraftToPoint(point, toolDown: true);
            bool added = solved && Editor.AddRobotPose(name) is null;
            GD.Print($"SELFTEST robot teach {name} at {point}: solved={solved} added={added} angles={Editor.RobotDraft}");
            ok &= added;
        }

        IReadOnlyList<RobotPose> poses = Editor.SelectedRobotPoses;
        ok &= poses.Select(pose => pose.Number).SequenceEqual(new[] { 1, 2, 3, 4 });
        bool outOfReach = !Editor.SetRobotDraftToPoint(new Vector3d(1.5, 0.0, 0.0), toolDown: true);
        Editor.SelectRobotPose(0);
        bool homeLocked = Editor.SelectedRobotPose == 0 && Editor.RobotDraft == RobotAngles.Home
            && Editor.OverwriteRobotPose() is not null && Editor.RemoveRobotPose() is not null
            && Editor.SelectedRobotPoses.Count == 4;
        Editor.SelectRobotPose(2);
        bool goTo = Editor.SelectedRobotPose == 2 && Robot() is { } arm && arm.Controller.Angles == poses[1].Angles;
        GD.Print($"SELFTEST robot teach: poses={poses.Count} outOfReachRefused={outOfReach} homeLocked={homeLocked} goTo={goTo}");
        return ok && outOfReach && homeLocked && goTo;
    }

    /// <summary>Saves the scene, wipes it and opens it again; true when the positions are still there.</summary>
    private bool SaveAndOpenRobot()
    {
        string saved = "user://selftest/robot_test" + SceneEditor.FileExtension;
        string before = Main.Scene.ToJson();
        _robotSavedPoses = string.Join("; ", Main.Scene.Find(_robotId!)?.Poses?.Select(pose => $"{pose.Number} {pose.Angles}") ?? Array.Empty<string>());
        bool save = Editor.SaveTo(saved) is null;
        Editor.NewScene();
        bool load = Editor.LoadFrom(saved) is null && Main.Scene.ToJson() == before;
        string after = string.Join("; ", Main.Scene.Find(_robotId!)?.Poses?.Select(pose => $"{pose.Number} {pose.Angles}") ?? Array.Empty<string>());
        GD.Print($"SELFTEST robot files: save={save} load={load} poses={after}");
        return save && load && after == _robotSavedPoses && after.Length > 0;
    }

    /// <summary>
    /// A robot arm on its pedestal between two belts, built with the editor. First one item runs to
    /// the stop so the test knows where to pick; then four positions are taught with the editor's
    /// solver, the scene is saved and opened again, and a PLC program on tags moves four items from
    /// belt 1 to belt 2. A go to a position that was not taught gives an error; reset puts the arm home.
    /// </summary>
    private void RunRobot(double delta)
    {
        _wallClock += delta;
        RobotArm? robot = Robot();

        switch (_controlsPhase)
        {
            case 0:
            {
                bool built = BuildRobotLine();
                Editor.SetDesignMode(false);
                string[] actuators = { "pos 1", "pos 2", "pos 4", "pos 8", "go", "grip" };
                string[] sensors = { "busy", "at pos", "item", "error" };
                SortersCheck("built", built
                    && actuators.All(suffix => Main.Tags.ActuatorNames.Contains(RobotTag(suffix)))
                    && sensors.All(suffix => Main.Tags.SensorNames.Contains(RobotTag(suffix)))
                    && new[] { "Conveyor", "Emitter", "Stop" }.All(Main.Tags.ActuatorNames.Contains)
                    && Main.Tags.SensorNames.Contains("Sensor"));
                _robotPlan = BuildRobotProbe();
                _robotIndex = 0;
                _robotStepSince = _elapsed;
                Main.Camera.Frame(new Vector3(0.0f, 1.0f, -0.2f), 3.5f);
                Main.Camera.SetView(150.0f, -30.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _robotPlan is not null && _robotIndex >= _robotPlan.Count:
            {
                Box? first = GetTree().GetNodesInGroup(Box.Group).OfType<Box>().Where(box => !box.IsQueuedForDeletion())
                    .OrderByDescending(box => box.GlobalPosition.X).FirstOrDefault();
                bool probed = first is not null && robot is not null && first.LinearVelocity.Length() < 0.05f;
                if (probed)
                {
                    Vector3 top = first!.GlobalPosition + new Vector3(0.0f, first.Height / 2.0f, 0.0f);
                    _robotPick = robot!.WorldToFoot(top);
                }

                GD.Print($"SELFTEST robot probe: item at {first?.GlobalPosition}, pick {_robotPick} (from the foot)");
                SortersCheck("item rests at the stop", probed && Math.Abs(_robotPick.Z + 0.5) < 0.05);
                Editor.SetDesignMode(true);
                bool taught = TeachRobot();
                SortersCheck("taught four positions", taught);
                SortersCheck("save and open keep the positions", SaveAndOpenRobot());
                Editor.SetDesignMode(false);
                SortersCheck("at home", Robot() is { Holding: false } arm && arm.Controller.Angles == RobotAngles.Home
                    && Main.CountBoxes().InScene == 0);
                _robotPlan = BuildRobotPlan();
                _robotIndex = 0;
                _robotStepSince = _elapsed;
                _wallClock = 0.0;
                Main.Sim.Run();
                _controlsPhase = 2;
                break;
            }

            case 1 when _wallClock >= 30.0:
                GD.Print("SELFTEST robot: no item reached the stop");
                SortersCheck("probe finished in time", false);
                _controlsPhase = 9;
                break;

            case 2 when _robotPlan is not null && _robotIndex >= _robotPlan.Count:
                GD.Print($"SELFTEST robot: plan finished at {_elapsed:0.0} s");
                Main.Sim.Reset();
                _wallClock = 1000.0;
                _controlsPhase = 3;
                break;

            case 2 when _wallClock >= 240.0:
                GD.Print($"SELFTEST robot: stuck in step {_robotIndex} ({_robotPlan?[Math.Min(_robotIndex, _robotPlan.Count - 1)].Label}), "
                    + $"angles={robot?.Controller.Angles} holding={robot?.Holding}");
                SortersCheck("sequence finished in time", false);
                _controlsPhase = 9;
                break;

            case 3 when _wallClock >= 1001.0:
                SortersCheck("reset", robot is { Holding: false, ItemDetected: false } && !robot.Controller.Busy && !robot.Controller.Error
                    && robot.Controller.Angles == RobotAngles.Home && Main.CountBoxes().InScene == 0);
                _controlsPhase = 9;
                break;

            case 9:
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }
}
