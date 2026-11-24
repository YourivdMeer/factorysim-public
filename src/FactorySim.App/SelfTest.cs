using System;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core.Scenes;
using FactorySim.Core.Plc;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Unattended run of the scene, started with user arguments after "--":
///   --selftest=sort:20     sort 20 boxes on height
///   --selftest=soak:600    run the belt for 600 s of simulated time without pushing
///   --selftest=controls    pause, resume and reset, then the optional tags Sim paused and Sim reset
///   --selftest=plc:50      sort under control of a Modbus server (--plc-port=5020), or of the built-in test PLC (--test-plc)
///   --selftest=editor      design, save, open, export, import and run a scene
///   --selftest=line        a line with a bend and a stop
///   --selftest=panel       push buttons, emergency stop, selector, lamps, potentiometer, display and siren
///   --selftest=tank        tank: a two-point control on Int tags keeps the scene between two limits
///   --selftest=sorters     pop-up wheel sorter and pivot arm sorter
///   --selftest=heavy       turntable and chain transfer on a roller line
///   --selftest=station     pick and place stations with two and three axes
///   --selftest=lift        elevator between two levels
///   --selftest=palletizer  palletizer: two layers of boxes onto a pallet
///   --selftest=machining   machining center: raw material into bases and lids
///   --selftest=crane       stacker crane: pallets into a rack and out again, target and jog mode
///   --selftest=assembly    emitters that emit a chosen item; a lid set on a base becomes a final product
///   --selftest=sensors     capacitive, inductive, reflection and light array sensors, encoder, safety door
///   --selftest=transport   spur, scale and gate conveyors, the inclined belt, chutes, aligner, corner stop, free rollers, positioning bars, roller bend and roller stop
///   --selftest=robot       six-axis robot arm: teach four positions, move four items between two belts, save and open
///   --selftest=stats       production figures: throughput, lead time, on-time, reset, tab Cijfers and its CSV
///   --selftest=bidir       a two-way belt back and forth, both outputs on, a bistable pusher with pulses and both coils on
///   --selftest=decor       decor pieces with colliders: boxes on a platform, switchboard, column and stairs, a safeguard
///   --selftest=snap        belt, curve and belt clicked together with the editor (plan 17), then 10 boxes over them
///   --selftest=blocks      building blocks (plan 18): save a station as a block, place it twice, sort 10 boxes with the example
///   --selftest=api         test API (plan 21): tools/api/run_test.py drives the app against the fake PLC (--plc-port=5020)
///   --selftest=ui          window layout, tag table, I/O list, library, properties, undo, copy, overlap, welcome, crash report
///   --screenshot=path.png  save a frame after a few seconds (needs a window)
/// Prints SELFTEST lines and quits with exit code 0 (pass) or 1 (fail).
///
/// The self-test plays the PLC: it only reads sensor tags and writes actuator tags,
/// through the same store calls a PLC link uses.
/// </summary>
public partial class SelfTest : Node
{
    private enum Step
    {
        Transport,
        Push,
        Return,
    }

    private const double SettleSeconds = 1.0;
    private const double DefaultScreenshotAtSeconds = 12.0;

    private readonly string _mode;
    private readonly int _amount;
    private readonly string? _screenshotPath;

    private Step _step = Step.Transport;
    private double _elapsed;
    private double _finishedAt = -1.0;
    private int _maxInScene;
    private int _maxFallen;
    private bool _screenshotTaken;
    private bool _reported;

    private double _plcConnectedAt = -1.0;
    private bool _editorOk;
    private bool _editorSensorSeen;
    private bool _lineStopRaised;
    private bool _lineHeld;
    private int _stationStep;
    private double _stationSince;
    private bool _stationHasY;
    private int _stationCycles;
    private int _palletRows;
    private int _palletLayers;
    private double _rowFullSince;
    private readonly System.Collections.Generic.HashSet<ulong> _machBases = new();
    private readonly System.Collections.Generic.HashSet<ulong> _machLids = new();
    private double _machWaitedSince = -1.0;
    private bool _machWaited;
    private bool _heavyTable;
    private bool _heavyDivert;
    private int _heavyStep;
    private double _heavyClearAt;
    private bool _sortersOk = true;
    private string[] _sorterTagsOff = Array.Empty<string>();
    private int _countA;
    private int _countB;
    private bool _panelOk = true;
    private string[] _panelLampsOn = Array.Empty<string>();
    private Vector2 _panelClick;
    private int _panelDisplay;
    private int _scaleMaxWeight;
    private int _lightArrayMaxCount;
    private readonly System.Collections.Generic.HashSet<int> _visionNumbers = new();
    private bool _visionDetected;
    private bool _tankOk = true;
    private bool _tankFilling;
    private bool _tankReachedHigh;
    private int _tankSwitches;
    private double _tankLowest = double.MaxValue;
    private double _tankHighest;
    private double _tankMaxFlow;
    private double _tankStartedAt = -1.0;
    private bool _tankPlcOn;
    private double _wallClock;
    private System.Collections.Generic.List<CraneStep>? _cranePlan;
    private int _craneIndex;
    private double _craneStepSince;
    private double _transportSince;
    private int _alignerBefore;
    private double _alignerBeforeSum;
    private int _alignerAfter;
    private double _alignerAfterMax;
    private int _clampStep;
    private double _clampAt;
    private int _clampCycles;
    private double _clampWorst;
    private bool _stopChecked;
    private bool _stopHeld;
    private int _asmStep;
    private double _asmStepAt;
    private bool _asmLidReady;
    private bool _asmBaseReady;
    private double _asmLidSeenAt = -1.0;
    private double _asmBaseSeenAt = -1.0;
    private double _asmLidPulseUntil = 0.3;
    private double _asmBasePulseUntil = 0.3;
    private double _asmClearAt = -1.0;
    private int _asmCycles;
    private double _asmDoneAt = -1.0;
    private int _asmLoose;
    private readonly System.Collections.Generic.HashSet<ulong> _asmFinals = new();
    private readonly System.Collections.Generic.HashSet<ulong> _asmLoosened = new();
    private double _sensStart = -1.0;
    private bool _sensForward = true;
    private readonly System.Collections.Generic.Dictionary<string, bool> _sensPrevious = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _sensEdges = new();
    private readonly System.Collections.Generic.HashSet<ulong> _sensMetal = new();
    private readonly System.Collections.Generic.HashSet<ulong> _sensOther = new();
    private readonly System.Collections.Generic.HashSet<ulong> _sensLane2 = new();
    private readonly System.Collections.Generic.HashSet<ulong> _sensLane3 = new();
    private int _controlsPhase;
    private Vector3 _positionsBefore;
    private int _boxesBefore;
    private bool _pauseOk;
    private bool _resumeOk;
    private bool _controlsResetOk;

    public SelfTest(string mode, int amount, string? screenshotPath)
    {
        _mode = mode;
        _amount = amount;
        _screenshotPath = screenshotPath;
    }

    public required Main Main { get; init; }

    public required SceneEditor Editor { get; init; }

    // Used only to judge the result, never to control the scene. These are parts of the MVP scene.
    private Emitter Emitter => Main.View.Emitters[0];

    private Remover EndRemover => Main.View.Find<Remover>("End remover")!;

    private Remover LaneRemover => Main.View.Find<Remover>("Lane remover")!;

    /// <summary>Parses the user arguments; returns null when no self-test was requested.</summary>
    public static (string Mode, int Amount, string? Screenshot)? ParseArguments(string[] args)
    {
        string? mode = null;
        int amount = 0;
        string? screenshot = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--selftest=", StringComparison.Ordinal))
            {
                string[] parts = arg["--selftest=".Length..].Split(':');
                mode = parts[0];
                amount = parts.Length > 1 && int.TryParse(parts[1], out int value) ? value : 20;
            }
            else if (arg.StartsWith("--screenshot=", StringComparison.Ordinal))
            {
                screenshot = arg["--screenshot=".Length..];
            }
        }

        if (mode is null && screenshot is null)
        {
            return null;
        }

        return (mode ?? "sort", mode is null ? 3 : amount, screenshot);
    }

    /// <summary>Simulated time at which --screenshot saves its frame.</summary>
    public double ScreenshotAtSeconds { get; init; } = DefaultScreenshotAtSeconds;

    public static double ParseScreenshotTime(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--screenshot-at=", StringComparison.Ordinal)
                && double.TryParse(arg["--screenshot-at=".Length..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                return seconds;
            }
        }

        return DefaultScreenshotAtSeconds;
    }

    /// <summary>Port of the fake PLC for the "plc" mode.</summary>
    public int PlcPort { get; init; } = 5020;

    /// <summary>True when the "plc" mode uses the built-in test PLC (plan 29) instead of a server on --plc-port.</summary>
    public bool UseTestPlc { get; init; }

    public static bool ParseTestPlc(string[] args) => Array.IndexOf(args, "--test-plc") >= 0;

    public static int ParsePlcPort(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--plc-port=", StringComparison.Ordinal)
                && int.TryParse(arg["--plc-port=".Length..], out int port))
            {
                return port;
            }
        }

        return 5020;
    }

    public override void _Ready()
    {
        GD.Print($"SELFTEST start mode={_mode} amount={_amount}");
        if (_mode is "editor" or "line" or "panel" or "sorters" or "heavy" or "station" or "lift" or "palletizer" or "machining" or "crane" or "transport" or "sensors" or "assembly" or "robot" or "tank" or "ui")
        {
            return;     // these tests drive design mode themselves, from _Process
        }

        if (_mode == "timescale")
        {
            StartTimeScale();
            return;
        }

        if (_mode == "trace")
        {
            StartTrace();
            return;
        }

        if (_mode is "stats" or "bidir" or "decor" or "snap" or "blocks" or "api")
        {
            return;     // builds its line in design mode, from _Process
        }

        Main.Sim.Run();
        if (_mode == "plc" && UseTestPlc)
        {
            // As a tester does it: the button in the PLC window.
            Main.Hud.TestPlcButton.EmitSignal(BaseButton.SignalName.Pressed);
            GD.Print($"SELFTEST plc: built-in test PLC on port {Main.TestPlc?.Port}");
        }
        else if (_mode == "plc")
        {
            Main.ConnectPlc(new ModbusLinkOptions { Port = PlcPort });
        }
    }

    /// <summary>
    /// The scene under control of a real Modbus server (the fake PLC in sorter mode):
    /// press Start once the link is up, let the PLC sort, press Stop when enough boxes are done.
    /// </summary>
    private bool RunPlc()
    {
        if (Main.Link?.Status != LinkStatus.Connected)
        {
            // Give up if the PLC never shows up.
            return _plcConnectedAt < 0.0 && _elapsed > 10.0;
        }

        if (_plcConnectedAt < 0.0)
        {
            _plcConnectedAt = _elapsed;
        }

        int removed = EndRemover.Count + LaneRemover.Count;
        if (removed >= _amount)
        {
            Main.StartOverride = false;
            Main.StopOverride = true;
            return true;
        }

        // A short pulse on Start, like an operator pressing the button.
        Main.StartOverride = _elapsed - _plcConnectedAt < 0.3;
        return _elapsed > _amount * 6.0 + 30.0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode == "timescale")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                _elapsed += delta;
                RunTimeScalePhysics();
            }

            return;
        }

        if (_mode == "trace")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                _elapsed += delta;
                RunTracePhysics();
            }

            return;
        }

        if (_mode == "stats")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                RunStatsPhysics();
            }

            return;
        }

        if (_mode == "bidir")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                RunBidirPhysics();
            }

            return;
        }

        if (_mode == "decor")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                RunDecorPhysics();
            }

            return;
        }

        if (_mode == "snap")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                RunSnapPhysics();
            }

            return;
        }

        if (_mode == "blocks")
        {
            if (!_reported && Main.Sim.IsRunning)
            {
                RunBlocksPhysics();
            }

            return;
        }

        if (_mode == "api")
        {
            return;     // the PLC behind the API plays the line; the script drives the app
        }

        if (_reported || !Main.Sim.IsRunning)
        {
            return;
        }

        _elapsed += delta;
        (int inScene, int fallen) = Main.CountBoxes();
        _maxInScene = Math.Max(_maxInScene, inScene);
        _maxFallen = Math.Max(_maxFallen, fallen);

        if (_mode == "controls")
        {
            // Just keep the line moving; _Process drives pause, resume and reset.
            WriteActuators(conveyor: true, pusher: false, emitter: true);
            return;
        }

        if (_mode == "heavy")
        {
            RunHeavyPlc();
            return;
        }

        if (_mode == "station")
        {
            RunStationPlc();
            return;
        }

        if (_mode == "lift")
        {
            RunLiftPlc();
            return;
        }

        if (_mode == "palletizer")
        {
            RunPalletizerPlc();
            return;
        }

        if (_mode == "machining")
        {
            RunMachiningPlc();
            return;
        }

        if (_mode == "crane")
        {
            RunCranePlc();
            return;
        }

        if (_mode == "robot")
        {
            RunRobotPlc();
            return;
        }

        if (_mode == "tank")
        {
            RunTankPlc();
            return;
        }

        if (_mode == "transport")
        {
            RunTransportPlc();
            return;
        }

        if (_mode == "sensors")
        {
            RunSensorsPlc();
            return;
        }

        if (_mode == "assembly")
        {
            RunAssemblyPlc();
            return;
        }

        if (_mode == "sorters")
        {
            // Play the PLC: everything on, except the tags the current phase keeps off.
            var sorterTags = Main.Tags.ActuatorNames;
            Main.Tags.ApplyActuators(sorterTags.Select(name => !_sorterTagsOff.Contains(name)).ToArray());
            return;
        }

        if (_mode == "panel")
        {
            // Play the PLC: switch on exactly the lamps the current phase asks for.
            var lampTags = Main.Tags.ActuatorNames;
            Main.Tags.ApplyActuators(lampTags.Select(name => _panelLampsOn.Contains(name)).ToArray());
            Main.Tags.ApplyActuatorNumbers(Main.Tags.AnalogActuatorNames.Select(name => name == "Digital display" ? _panelDisplay : 0).ToArray());
            return;
        }

        if (_mode == "line")
        {
            // Play the PLC: everything runs; the stop is up for the first part of the test.
            var tagNames = Main.Tags.ActuatorNames;
            var values = new bool[tagNames.Count];
            for (int i = 0; i < tagNames.Count; i++)
            {
                values[i] = tagNames[i] != "Stop" || _lineStopRaised;
            }

            Main.Tags.ApplyActuators(values);
            return;
        }

        if (_mode == "editor")
        {
            // Play the PLC for the designed line: everything on, and watch the sensor.
            var names = Main.Tags.ActuatorNames;
            Main.Tags.ApplyActuators(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(true, names.Count)));
            bool[] sensors = Main.Tags.SnapshotSensors();
            for (int i = 0; i < sensors.Length; i++)
            {
                _editorSensorSeen |= sensors[i] && Main.Tags.SensorNames[i] == "Sensor";
            }

            return;
        }

        bool done = _mode switch
        {
            "soak" => RunSoak(),
            "plc" => RunPlc(),
            _ => RunSort(),
        };
        if (done && _finishedAt < 0.0)
        {
            _finishedAt = _elapsed;
        }

        // Let the last frames settle (queued frees, counters) before reporting.
        if (_finishedAt >= 0.0 && _elapsed - _finishedAt > SettleSeconds)
        {
            Report();
        }
    }

    public override void _Process(double delta)
    {
        if (_mode == "timescale" && !_reported)
        {
            RunTimeScaleFrame();
        }

        if (_mode == "trace" && !_reported)
        {
            RunTraceFrame();
        }

        if (_mode == "stats" && !_reported)
        {
            RunStatsFrame();
        }

        if (_mode == "bidir" && !_reported)
        {
            RunBidirFrame();
        }

        if (_mode == "decor" && !_reported)
        {
            RunDecorFrame();
        }

        if (_mode == "snap" && !_reported)
        {
            RunSnapFrame();
        }

        if (_mode == "blocks" && !_reported)
        {
            RunBlocksFrame();
        }

        if (_mode == "api" && !_reported)
        {
            RunApiFrame();
        }

        if (_mode == "controls" && !_reported)
        {
            RunControls(delta);
        }

        if (_mode == "editor" && !_reported)
        {
            RunEditor(delta);
        }

        if (_mode == "line" && !_reported)
        {
            RunLine(delta);
        }

        if (_mode == "panel" && !_reported)
        {
            RunPanel(delta);
        }

        if (_mode == "sorters" && !_reported)
        {
            RunSorters(delta);
        }

        if (_mode == "heavy" && !_reported)
        {
            RunHeavy(delta);
        }

        if (_mode == "station" && !_reported)
        {
            RunStation(delta);
        }

        if (_mode == "lift" && !_reported)
        {
            RunLift(delta);
        }

        if (_mode == "palletizer" && !_reported)
        {
            RunPalletizer(delta);
        }

        if (_mode == "machining" && !_reported)
        {
            RunMachining(delta);
        }

        if (_mode == "crane" && !_reported)
        {
            RunCrane(delta);
        }

        if (_mode == "robot" && !_reported)
        {
            RunRobot(delta);
        }

        if (_mode == "tank" && !_reported)
        {
            RunTank(delta);
        }

        if (_mode == "transport" && !_reported)
        {
            RunTransport(delta);
        }

        if (_mode == "sensors" && !_reported)
        {
            RunSensors(delta);
        }

        if (_mode == "assembly" && !_reported)
        {
            RunAssembly(delta);
        }

        if (_mode == "ui" && !_reported)
        {
            RunUi(delta);
        }

        if (_screenshotPath is null || _screenshotTaken || _elapsed < ScreenshotAtSeconds)
        {
            return;
        }

        _screenshotTaken = true;
        Error error = GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GD.Print($"SELFTEST screenshot {_screenshotPath}: {error}");
    }

    /// <summary>The sorting logic of the acceptance PLC program, on tags only.</summary>
    private bool RunSort() => RunSortFor(_amount);

    private bool RunSortFor(int amount)
    {
        TagStore tags = Main.Tags;
        bool[] sensors = tags.SnapshotSensors();
        bool atPusher = Sensor(sensors, TagNames.AtPusher);
        bool highBox = Sensor(sensors, TagNames.HighBox);
        bool pusherBack = Sensor(sensors, TagNames.PusherBack);
        bool pusherFront = Sensor(sensors, TagNames.PusherFront);

        switch (_step)
        {
            case Step.Transport:
                if (atPusher && highBox && pusherBack)
                {
                    _step = Step.Push;
                }

                break;
            case Step.Push:
                if (pusherFront)
                {
                    _step = Step.Return;
                }

                break;
            case Step.Return:
                if (pusherBack)
                {
                    _step = Step.Transport;
                }

                break;
        }

        WriteActuators(
            conveyor: _step == Step.Transport,
            pusher: _step == Step.Push,
            emitter: Emitter.EmittedCount < amount);

        int removed = EndRemover.Count + LaneRemover.Count;
        bool timedOut = _elapsed > amount * 6.0 + 30.0;
        return removed >= amount || timedOut;
    }

    private bool RunSoak()
    {
        WriteActuators(conveyor: true, pusher: false, emitter: true);
        return _elapsed >= _amount;
    }

    /// <summary>
    /// Checks pause and reset on wall-clock time: run 6 s, pause 2 s (nothing may move),
    /// resume 2 s (boxes must move again), reset (scene must be empty and stopped); then the
    /// optional simulation tags of plan 14 (<see cref="RunSimTags"/>).
    /// </summary>
    private void RunControls(double delta)
    {
        _wallClock += delta;
        switch (_controlsPhase)
        {
            case 0 when _wallClock >= 6.0:
                CheckSimTagsAbsent();
                _positionsBefore = BoxPositionSum();
                _boxesBefore = Main.CountBoxes().InScene;
                Main.Sim.Pause();
                _controlsPhase = 1;
                break;
            case 1 when _wallClock >= 8.0:
                _pauseOk = _boxesBefore > 0
                    && Main.CountBoxes().InScene == _boxesBefore
                    && _positionsBefore.DistanceTo(BoxPositionSum()) < 1e-4f;
                Main.Sim.Run();
                _controlsPhase = 2;
                break;
            case 2 when _wallClock >= 10.0:
                _resumeOk = _positionsBefore.DistanceTo(BoxPositionSum()) > 0.1f;
                Main.Sim.Reset();
                _controlsPhase = 3;
                break;
            case 3 when _wallClock >= 11.0:
                bool resetOk = Main.CountBoxes().InScene == 0
                    && Emitter.EmittedCount == 0
                    && EndRemover.Count + LaneRemover.Count == 0
                    && Main.Sim.State == FactorySim.Core.SimState.Stopped;
                _controlsResetOk = resetOk;
                GD.Print($"SELFTEST controls: pause={_pauseOk} resume={_resumeOk} reset={resetOk}");
                _controlsPhase = 4;
                break;
            case 4 when RunSimTags():
            {
                _reported = true;
                bool pass = _pauseOk && _resumeOk && _controlsResetOk && _simTagsOk;
                GD.Print($"SELFTEST controls sim tags: {_simTagsOk}");
                GD.Print(pass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(pass ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>
    /// Builds a small line with the editor's own operations, saves it, reloads it, exports and
    /// imports it, and then runs it: boxes must travel from the emitter to the remover.
    /// </summary>
    private void RunEditor(double delta)
    {
        _wallClock += delta;
        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool placed = Editor.Place("belt_conveyor_4m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", -1.5f, 0.0f) is not null
                    && Editor.Place("remover_hood_1m0", 2.3f, 0.0f) is not null
                    && Editor.Place("diffuse_sensor", 1.0f, -0.6f) is not null
                    && Editor.Place("electric_switchboard", 0.0f, 3.0f) is not null  // visual only
                    && Editor.Place("roller_conveyor_2m", -3.0f, -3.0f) is not null;
                SceneItem? extra = Editor.Place("column", 3.0f, 3.0f);
                Editor.RotateSelected();                                         // column: 90 degrees
                Editor.MoveSelected(3.6f, 2.4f);                                 // snaps to 3.5 / 2.5
                SceneItem? moved = extra is null ? null : Main.Scene.Find(extra.Id);
                bool edited = moved is { Yaw: 90.0, X: 3.5, Z: 2.5 };
                SceneItem? doomed = Editor.Place("stack_light", -3.0f, 3.0f);
                Editor.DeleteSelected();
                bool deleted = doomed is not null && Main.Scene.Find(doomed.Id) is null;
                bool rejected = Editor.Place("box_small", 0.0f, 0.0f) is null;   // items cannot be placed
                _editorOk = placed && edited && deleted && rejected && Main.Scene.Items.Count == 7;
                GD.Print($"SELFTEST editor: placed={placed} edited={edited} deleted={deleted} rejected={rejected} "
                    + $"items={Main.Scene.Items.Count}");
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 1.0:
            {
                if (_screenshotPath is not null)
                {
                    Error error = GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
                    GD.Print($"SELFTEST screenshot {_screenshotPath}: {error}");
                    _screenshotTaken = true;
                }

                // Save to the own folder, wipe, open again.
                string saved = "user://selftest/editor_test" + SceneEditor.FileExtension;
                bool save = Editor.SaveTo(saved) is null;
                string before = Main.Scene.ToJson();
                Editor.NewScene();
                bool wiped = Main.Scene.Items.Count == 0;
                bool load = Editor.LoadFrom(saved) is null && Main.Scene.ToJson() == before;

                // Export to a path outside the app, wipe, import again.
                string exported = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "factorysim_export_test" + SceneEditor.FileExtension);
                bool export = Editor.SaveTo(exported) is null && System.IO.File.Exists(exported);
                Editor.NewScene();
                bool import = Editor.LoadFrom(exported) is null && Main.Scene.ToJson() == before;
                bool badFile = Editor.LoadFrom(ProjectSettings.GlobalizePath("res://project.godot")) is not null
                    && Main.Scene.ToJson() == before;                           // a wrong file changes nothing
                _editorOk &= save && wiped && load && export && import && badFile;
                GD.Print($"SELFTEST editor files: save={save} wiped={wiped} load={load} export={export} import={import} "
                    + $"badFileRejected={badFile}");
                _editorOk &= RunEditorAutosave(exported);                      // plan 16

                Editor.SetDesignMode(false);
                Main.Sim.Run();
                _controlsPhase = 2;
                break;
            }

            case 2 when _wallClock >= 13.0:
            {
                // Twelve seconds of running the designed line.
                Remover? remover = Main.View.Find<Remover>("Remover");
                bool sensorSeen = _editorSensorSeen;
                (int _, int fallen) = Main.CountBoxes();
                bool ran = remover is { Count: >= 2 } && fallen == 0 && sensorSeen;
                GD.Print($"SELFTEST editor run: removed={remover?.Count} fallen={fallen} sensorSeen={sensorSeen} "
                    + $"tags={Main.TagMap.Entries.Count}");
                _editorOk &= ran;

                // Plan 20: the scene gallery, then the window with the examples for the screenshot.
                Editor.SetDesignMode(true);
                _editorOk &= RunEditorGallery();
                Editor.ShowGalleryWith(Editor.GalleryEntries(GalleryFolder));
                _controlsPhase = 3;
                break;
            }

            case 3 when _wallClock >= 14.0:
            {
                SaveScreenshot(suffix: "_gallery");
                Editor.HideGallery();
                _reported = true;
                GD.Print(_editorOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_editorOk ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>
    /// A line with a bend and a stop: emitter -> belt -> curve -> belt -> stop blade -> remover.
    /// While the stop is up nothing may reach the remover and boxes must queue; after it drops
    /// they must arrive. No box may fall, which also proves the bend carries them round.
    /// </summary>
    private void RunLine(double delta)
    {
        _wallClock += delta;
        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                // Belt A runs along +X and ends at x = 1. The bend (radius 1) takes over there and
                // turns the flow towards -Z; belt B carries on from (2, -1) down to z = -3.
                bool built = Editor.Place("belt_conveyor_2m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", -0.5f, 0.0f) is not null
                    && Editor.Place("belt_conveyor_curved", 1.0f, -1.0f, 270.0f) is not null
                    && Editor.Place("belt_conveyor_2m", 2.0f, -2.0f, 90.0f) is not null
                    && Editor.Place("stop_blade", 2.0f, -2.5f, 90.0f) is not null
                    && Editor.Place("remover_hood_1m0", 2.0f, -3.25f, 90.0f) is not null;
                Editor.SetDesignMode(false);
                _editorOk = built && Main.TagMap.Entries.Any(tag => tag.Name == "Stop")
                    && Main.TagMap.Entries.Any(tag => tag.Name == "Curve");
                GD.Print($"SELFTEST line: built={built} tags={string.Join(", ", Main.Tags.ActuatorNames)}");
                _lineStopRaised = true;
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 16.0:
            {
                Remover? remover = Main.View.Find<Remover>("Remover");
                (int inScene, int fallen) = Main.CountBoxes();
                _lineHeld = remover is { Count: 0 } && inScene >= 3 && fallen == 0;
                GD.Print($"SELFTEST line, stop up: removed={remover?.Count} waiting={inScene} fallen={fallen}");
                if (_screenshotPath is not null)
                {
                    Error error = GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
                    GD.Print($"SELFTEST screenshot {_screenshotPath}: {error}");
                    _screenshotTaken = true;
                }

                _lineStopRaised = false;
                _controlsPhase = 2;
                break;
            }

            case 2 when _wallClock >= 30.0:
            {
                Remover? remover = Main.View.Find<Remover>("Remover");
                (int inScene, int fallen) = Main.CountBoxes();
                bool released = remover is { Count: >= 4 } && fallen == 0;
                GD.Print($"SELFTEST line, stop down: removed={remover?.Count} onLine={inScene} fallen={fallen} "
                    + $"emitted={Main.View.Emitters.Sum(e => e.EmittedCount)}");
                _reported = true;
                bool pass = _editorOk && _lineHeld && released;
                GD.Print(pass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(pass ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>
    /// An operator panel built with the editor: push button, emergency stop, selector, signal
    /// lamp, stack light and warning light. Checks the tags they give, what each control does to
    /// its sensor tag (through the part and through a click on screen), that the lamps follow
    /// their actuator tags one by one, and that a scene reset returns everything to rest.
    /// </summary>
    private void RunPanel(double delta)
    {
        _wallClock += delta;
        OperatorControl? button = Main.View.Find<OperatorControl>("Button");
        OperatorControl? emergencyStop = Main.View.Find<OperatorControl>("Emergency stop");
        OperatorControl? selector = Main.View.Find<OperatorControl>("Selector");
        IndicatorLamp? lamp = Main.View.Find<IndicatorLamp>("Lamp");
        IndicatorLamp? stack = Main.View.Find<IndicatorLamp>("Stack light");
        IndicatorLamp? warning = Main.View.Find<IndicatorLamp>("Warning light");
        Potentiometer? pot = Main.View.Find<Potentiometer>("Potentiometer");
        DigitalDisplay? display = Main.View.Find<DigitalDisplay>("Digital display");
        Siren? siren = Main.View.Find<Siren>("Siren");

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("push_button_green", 0.0f, 0.0f) is not null
                    && Editor.Place("emergency_stop", 0.25f, 0.0f) is not null
                    && Editor.Place("selector", 0.5f, 0.0f) is not null
                    && Editor.Place("light_indicator_green", 0.75f, 0.0f) is not null
                    && Editor.Place("stack_light", 1.25f, 0.0f) is not null
                    && Editor.Place("warning_light", 1.75f, 0.0f) is not null
                    && Editor.Place("potentiometer", 2.25f, 0.0f) is not null
                    && Editor.Place("digital_display", 2.6f, 0.0f) is not null
                    && Editor.Place("alarm_siren", 3.0f, 0.0f) is not null;
                Editor.SetDesignMode(false);

                string[] sensors = { "Button", "Emergency stop", "Selector" };
                string[] actuators = { "Lamp", "Stack light green", "Stack light red", "Stack light yellow", "Warning light" };
                bool tags = sensors.All(name => Main.Tags.SensorNames.Contains(name))
                    && actuators.All(name => Main.Tags.ActuatorNames.Contains(name));
                PanelCheck("built", built && tags);
                PanelCheck("analog tags", Main.Tags.AnalogSensorNames.Contains("Potentiometer")
                    && Main.Tags.AnalogActuatorNames.Contains("Digital display") && Main.Tags.ActuatorNames.Contains("Siren"));
                GD.Print($"SELFTEST panel: sensors={string.Join(", ", Main.Tags.SensorNames)}");
                GD.Print($"SELFTEST panel: actuators={string.Join(", ", Main.Tags.ActuatorNames)}");

                // Headless there is no window and the viewport is 64 x 64 pixels: every control
                // would be within one click radius of the others. Give it the size of a window.
                if (GetViewport().GetVisibleRect().Size.X < 640.0f)
                {
                    GetTree().Root.Size = new Vector2I(1280, 720);
                }

                Main.Camera.Frame(new Vector3(1.4f, 1.3f, 0.0f), 3.6f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 0.5:
                // At rest: only the emergency stop gives a signal (normally closed). All lamps off.
                PanelCheck("rest", !PanelSensor("Button") && PanelSensor("Emergency stop") && !PanelSensor("Selector")
                    && lamp is not null && !lamp.IsOn(0) && stack is not null && !stack.IsOn(0) && !stack.IsOn(1) && !stack.IsOn(2));
                button?.Press();
                emergencyStop?.Press();
                selector?.Press();
                PanelCheck("analog rest", PanelNumber("Potentiometer") == 0 && display is { Number: 0 } && siren is { On: false });
                pot?.Step(4);
                _panelDisplay = 1234;
                _panelLampsOn = new[] { "Lamp", "Stack light red", "Warning light", "Siren" };
                _controlsPhase = 2;
                break;

            case 2 when _wallClock >= 1.0:
            {
                PanelCheck("operated", PanelSensor("Button") && !PanelSensor("Emergency stop") && PanelSensor("Selector"));

                // Stack light parts are ordered Lamp_Green, Lamp_Red, Lamp_Yellow.
                PanelCheck("lamps", lamp is not null && lamp.IsOn(0) && warning is not null && warning.IsOn(0)
                    && stack is not null && !stack.IsOn(0) && stack.IsOn(1) && !stack.IsOn(2));
                PanelCheck("analog", PanelNumber("Potentiometer") == 4 * FactorySim.Core.KnobLogic.StepSize
                    && display is { Number: 1234, Text: "1234" } && siren is { On: true });
                GD.Print($"SELFTEST panel: potentiometer={PanelNumber("Potentiometer")} display='{display?.Text}' siren={siren?.On}");
                if (_screenshotPath is not null)
                {
                    Error error = GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
                    GD.Print($"SELFTEST screenshot {_screenshotPath}: {error}");
                    _screenshotTaken = true;
                }

                button?.Release();
                emergencyStop?.Release();
                selector?.Release();
                _panelLampsOn = new[] { "Stack light green", "Stack light yellow" };
                _panelDisplay = -56;
                _controlsPhase = 3;
                break;
            }

            case 3 when _wallClock >= 1.5:
                // Letting go drops the push button; the latching controls stay where they are.
                PanelCheck("released", !PanelSensor("Button") && !PanelSensor("Emergency stop") && PanelSensor("Selector"));
                PanelCheck("lamps2", lamp is not null && !lamp.IsOn(0) && stack is not null && stack.IsOn(0) && !stack.IsOn(1) && stack.IsOn(2));
                PanelCheck("analog2", display is { Number: -56, Text: "-56" } && siren is { On: false });

                // Now the way the operator does it: a click where the button is drawn.
                _panelClick = button is null ? Vector2.Zero : Main.Camera.UnprojectPosition(button.GlobalPosition);
                PanelCheck("pick", button is not null && Main.ControlAt(_panelClick) == button
                    && Main.ControlAt(new Vector2(5.0f, 5.0f)) is null && !Main.Click(new Vector2(5.0f, 5.0f), pressed: true));
                Main.Click(new Vector2(5.0f, 5.0f), pressed: false);
                PanelCheck("click", Main.Click(_panelClick + new Vector2(6.0f, -4.0f), pressed: true));
                GD.Print($"SELFTEST panel: viewport={GetViewport().GetVisibleRect().Size} button drawn at {_panelClick}");
                _controlsPhase = 4;
                break;

            case 4 when _wallClock >= 2.0:
            {
                PanelCheck("clicked", PanelSensor("Button") && button is { Engaged: true });

                // Let go somewhere else: the button that was pressed must still come back.
                Main.Click(new Vector2(5.0f, 5.0f), pressed: false);

                // A click on the emergency stop resets it (it was latched in phase 1).
                Vector2 stopAt = emergencyStop is null ? Vector2.Zero : Main.Camera.UnprojectPosition(emergencyStop.GlobalPosition);
                Main.Click(stopAt, pressed: true);
                Main.Click(stopAt, pressed: false);

                // A click right of the knob turns the potentiometer one step up.
                Vector2 potAt = pot is null ? Vector2.Zero : Main.Camera.UnprojectPosition(pot.PickPoint);
                PanelCheck("pick knob", pot is not null && Main.PotentiometerAt(potAt) == pot);
                PanelCheck("click knob", Main.Click(potAt + new Vector2(6.0f, 0.0f), pressed: true));
                Main.Click(potAt + new Vector2(6.0f, 0.0f), pressed: false);
                _controlsPhase = 5;
                break;
            }

            case 5 when _wallClock >= 2.5:
                PanelCheck("unclicked", !PanelSensor("Button") && PanelSensor("Emergency stop") && PanelSensor("Selector"));
                PanelCheck("knob clicked", PanelNumber("Potentiometer") == 5 * FactorySim.Core.KnobLogic.StepSize);
                Main.Sim.Reset();
                _controlsPhase = 6;
                break;

            case 6 when _wallClock >= 3.0:
            {
                PanelCheck("reset", !PanelSensor("Button") && PanelSensor("Emergency stop") && !PanelSensor("Selector")
                    && selector is { Engaged: false } && stack is not null && !stack.IsOn(0) && !stack.IsOn(1) && !stack.IsOn(2));
                PanelCheck("analog reset", pot is { Value: 0 } && display is { Number: 0 } && siren is { On: false });
                _reported = true;
                GD.Print(_panelOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_panelOk ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>
    /// Two small lines built with the editor, each with a straight exit (remover "Remover") and
    /// a side lane (remover "Remover 2"). First the wheel sorter: straight on, then to the left.
    /// Then the pivot arm: arm back, then arm out. In each phase the boxes must arrive at the
    /// right remover only, and none may fall.
    /// </summary>
    private void RunSorters(double delta)
    {
        _wallClock += delta;
        Remover? straight = Main.View.Find<Remover>("Remover");
        Remover? side = Main.View.Find<Remover>("Remover 2");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
            {
                // Belt along +X, wheel sorter at x = 1..2, belt on to the straight remover.
                // The side lane leaves the sorter's left-hand edge (z = -0.4) towards -Z.
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("belt_conveyor_2m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", -0.5f, 0.0f) is not null
                    && Editor.Place("popup_wheel_sorter", 1.5f, 0.0f) is not null
                    && Editor.Place("belt_conveyor_2m", 3.0f, 0.0f) is not null
                    && Editor.Place("remover_hood_1m0", 4.25f, 0.0f) is not null
                    && Editor.Place("belt_conveyor_2m", 1.5f, -1.5f, 90.0f) is not null
                    && Editor.Place("remover_hood_1m0", 1.5f, -2.75f, 90.0f) is not null;
                Editor.SetDesignMode(false);
                string[] tags = { "Wheel sorter", "Wheel sorter left", "Wheel sorter right" };
                SortersCheck("wheel sorter built", built && tags.All(name => Main.Tags.ActuatorNames.Contains(name)));
                _sorterTagsOff = new[] { "Wheel sorter left", "Wheel sorter right" };
                Main.Camera.Frame(new Vector3(1.5f, 0.8f, -0.8f), 6.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 14.0:
                GD.Print($"SELFTEST sorters, wheels straight: straight={straight?.Count} side={side?.Count} fallen={fallen}");
                SortersCheck("wheels straight", straight is { Count: >= 2 } && side is { Count: 0 } && fallen == 0);
                _sorterTagsOff = new[] { "Wheel sorter right" };
                _controlsPhase = 2;
                break;

            case 2 when _wallClock >= 18.0:
                // Boxes that were already past the sorter have left by now; count from here.
                _countA = straight?.Count ?? 0;
                _countB = side?.Count ?? 0;
                _controlsPhase = 3;
                break;

            case 3 when _wallClock >= 30.0:
            {
                GD.Print($"SELFTEST sorters, wheels left: straight=+{straight?.Count - _countA} side=+{side?.Count - _countB} fallen={fallen}");
                SortersCheck("wheels left", straight is not null && straight.Count == _countA
                    && side is not null && side.Count - _countB >= 2 && fallen == 0);
                WheelSorter? wheels = Main.View.Find<WheelSorter>("Wheel sorter");
                Vector3 velocity = wheels?.SurfaceVelocity ?? Vector3.Zero;
                SortersCheck("wheels velocity", velocity.X > 0.3f && velocity.Z < -0.3f);
                SaveScreenshot(suffix: "_wheels");

                // Second line. The post stands 0.5 m to the -Z side of the belt centre; the arm
                // swings towards +Z and guides boxes off the far edge, onto a lane towards +Z.
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("belt_conveyor_4m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", -1.5f, 0.0f) is not null
                    && Editor.Place("remover_hood_1m0", 2.25f, 0.0f) is not null
                    && Editor.Place("pivot_arm_sorter", 0.0f, -0.5f) is not null
                    && Editor.Place("belt_conveyor_2m", 0.75f, 1.5f, 270.0f) is not null
                    && Editor.Place("remover_hood_1m0", 0.75f, 2.75f, 270.0f) is not null;
                Editor.SetDesignMode(false);
                SortersCheck("arm sorter built", built && Main.Tags.ActuatorNames.Contains("Arm sorter"));
                _sorterTagsOff = new[] { "Arm sorter" };
                Main.Camera.Frame(new Vector3(0.5f, 0.8f, 0.8f), 6.0f);
                Main.Sim.Run();
                _controlsPhase = 4;
                break;
            }

            case 4 when _wallClock >= 44.0:
                GD.Print($"SELFTEST sorters, arm back: straight={straight?.Count} side={side?.Count} fallen={fallen}");
                SortersCheck("arm back", straight is { Count: >= 2 } && side is { Count: 0 } && fallen == 0
                    && Main.View.Find<ArmSorter>("Arm sorter") is { IsBack: true });

                // Like a PLC program would: let the line run empty before the arm swings out.
                // A box that is in the way of the swinging arm gets knocked off the belt.
                _sorterTagsOff = new[] { "Arm sorter", "Emitter" };
                _controlsPhase = 5;
                break;

            case 5 when _wallClock >= 52.0:
                SortersCheck("line empty before the swing", Main.CountBoxes().InScene == 0);
                _countA = straight?.Count ?? 0;
                _countB = side?.Count ?? 0;
                _sorterTagsOff = Array.Empty<string>();
                _controlsPhase = 6;
                break;

            case 6 when _wallClock >= 66.0:
                GD.Print($"SELFTEST sorters, arm out: straight=+{straight?.Count - _countA} side=+{side?.Count - _countB} fallen={fallen}");
                SortersCheck("arm out", straight is not null && straight.Count == _countA
                    && side is not null && side.Count - _countB >= 2 && fallen == 0
                    && Main.View.Find<ArmSorter>("Arm sorter") is { IsOut: true });
                SaveScreenshot(suffix: "_arm");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// The window layout and the editor tools behind it, driven through the same operations as
    /// the buttons: panels, tag table with groups and filter, I/O list, library search,
    /// properties with parameters, undo/redo, copy, overlap warning and camera views.
    /// </summary>
    private void RunUi(double delta)
    {
        _wallClock += delta;
        Ui.Shell shell = Main.Shell;
        Ui.Hud hud = Main.Hud;
        switch (_controlsPhase)
        {
            case 0:
                if (GetViewport().GetVisibleRect().Size.X < 1000.0f)
                {
                    GetTree().Root.Size = new Vector2I(1600, 900);      // headless starts with a tiny viewport
                }

                _controlsPhase = 1;
                break;

            case 1 when _wallClock >= 0.5:
            {
                // Simulate mode with the default scene: the view is free, the tag panels are filled.
                Vector2 window = GetViewport().GetVisibleRect().Size;
                Rect2 free = shell.FreeRect;
                GD.Print($"SELFTEST ui: window={window} free={free} groups={string.Join(" | ", hud.Groups)} "
                    + $"io={hud.IoRowCounts} tags={Main.TagMap.Entries.Count} toolbar min width={shell.Toolbar.GetCombinedMinimumSize().X}");
                SortersCheck("view is free in simulate mode", free.Size.X > window.X * 0.9f && free.Size.Y > window.Y * 0.45f
                    && free.Position.Y > 20.0f && free.End.Y < window.Y - 100.0f);
                SortersCheck("tags grouped per part", hud.Groups.Count >= 4 && hud.Groups[0] == "Simulator"
                    && hud.Groups.Contains("Pusher") && hud.VisibleTags.Count == Main.TagMap.Entries.Count);
                SortersCheck("io list complete", hud.IoRowCounts.Inputs + hud.IoRowCounts.Outputs == Main.TagMap.Entries.Count
                    && hud.IoRowCounts.Inputs == Main.Tags.SensorNames.Count + Main.Tags.AnalogSensorNames.Count
                    && hud.IoRowCounts.Outputs == Main.Tags.ActuatorNames.Count + Main.Tags.AnalogActuatorNames.Count);

                hud.SetFilter(FactorySim.Core.Scenes.TagFilter.Actuators, string.Empty);
                bool actuators = hud.VisibleTags.Count == Main.Tags.ActuatorNames.Count + Main.Tags.AnalogActuatorNames.Count && hud.IoRowCounts.Inputs == 0;
                hud.SetFilter(FactorySim.Core.Scenes.TagFilter.All, "pusher");
                bool searched = hud.VisibleTags.Count > 0 && hud.VisibleTags.All(name => name.Contains("usher"));
                hud.SetFilter(FactorySim.Core.Scenes.TagFilter.Forced, string.Empty);
                bool noneForced = hud.VisibleTags.Count == 0;
                hud.SetForce(TagNames.Conveyor, 2);
                bool oneForced = hud.VisibleTags.Count == 1 && Main.Tags.Snapshot().Single(tag => tag.Name == TagNames.Conveyor).Forced;
                hud.ClearForces();
                hud.SetFilter(FactorySim.Core.Scenes.TagFilter.All, string.Empty);
                SortersCheck("filter and search", actuators && searched && noneForced && oneForced
                    && hud.VisibleTags.Count == Main.TagMap.Entries.Count);
                SaveScreenshot(suffix: "_simulate");
                hud.ShowIoList(true);
                _controlsPhase = 2;
                break;
            }

            case 2 when _wallClock >= 1.0:
                SaveScreenshot(suffix: "_iolist");
                hud.ShowIoList(false);
                Editor.SetDesignMode(true);
                _controlsPhase = 3;
                break;

            case 3 when _wallClock >= 1.5:
            {
                Vector2 window = GetViewport().GetVisibleRect().Size;
                Rect2 free = shell.FreeRect;
                GD.Print($"SELFTEST ui, design: free={free} palette={Editor.PaletteAssets.Count} toolbar min width={shell.Toolbar.GetCombinedMinimumSize().X}");
                SortersCheck("side panels in design mode", free.Position.X > 250.0f && free.End.X < window.X - 250.0f && free.Size.X > 500.0f);

                int all = Editor.PaletteAssets.Count;
                Editor.SetPaletteFilter(null, "pallet");
                bool search = Editor.PaletteAssets.Count is > 0 and < 6 && Editor.PaletteAssets.Any(asset => asset.Id == "palletizer");
                Editor.SetPaletteFilter("heavy_load", string.Empty);
                bool category = Editor.PaletteAssets.Count > 0 && Editor.PaletteAssets.All(asset => asset.Category == "heavy_load");
                Editor.SetPaletteFilter(null, string.Empty);
                SortersCheck("library search and category", search && category && Editor.PaletteAssets.Count == all
                    && all == Main.Catalog.Assets.Count(asset => asset.IsPlaceable));

                // Properties, undo and copy on a fresh scene.
                Editor.NewScene();
                SceneItem? belt = Editor.Place("belt_conveyor_2m", 0.0f, 0.0f);
                bool fields = Editor.ParamFieldNames.Contains("speed") && Editor.ParamFieldNames.Contains("alwaysOn");
                Editor.SetSelectedParam("speed", 0.3);
                Editor.SetSelectedYaw(90.0f);
                SceneItem? changed = belt is null ? null : Main.Scene.Find(belt.Id);
                bool edited = changed is { Yaw: 90.0 } && changed.Params is not null && changed.Params["speed"] == 0.3;
                SortersCheck("properties", fields && edited);

                bool undone = Editor.Undo() && Main.Scene.Find(belt!.Id) is { Yaw: 0.0 };
                undone &= Editor.Undo() && Main.Scene.Find(belt.Id)!.Params is null;
                undone &= Editor.Undo() && Main.Scene.Items.Count == 0 && !Editor.CanUndo;
                bool redone = Editor.Redo() && Editor.Redo() && Editor.Redo() && !Editor.CanRedo
                    && Main.Scene.Find(belt.Id) is { Yaw: 90.0 };
                SortersCheck("undo and redo", undone && redone);

                Editor.Select(belt.Id);
                SceneItem? copy = Editor.DuplicateSelected();
                bool copied = copy is { Name: "Conveyor 2", Yaw: 90.0, X: 0.5, Z: 0.5 } && copy.Params?["speed"] == 0.3;
                bool overlap = Editor.OverlapWarning.Contains("Conveyor 2");
                Editor.MoveSelected(3.0f, 0.5f);
                bool cleared = Editor.OverlapWarning.Length == 0;
                Editor.CopySelected();
                SceneItem? pasted = Editor.PasteAt(-3.0f, 2.0f);
                copied &= pasted is { Name: "Conveyor 3", X: -3.0, Z: 2.0 } && Main.Scene.Items.Count == 3;
                GD.Print($"SELFTEST ui: copied={copied} overlap={overlap} cleared={cleared} warning='{Editor.OverlapWarning}'");
                SortersCheck("copy and overlap warning", copied && overlap && cleared);

                Editor.Place("emitter_gantry", -0.5f, 0.0f);
                bool emitterFields = Editor.ParamFieldNames.Contains("highRatio") && Editor.ParamFieldNames.Contains("interval");

                // The list *Item* of the emitter: what fits through the gantry (no pallets), a choice, undo and redo.
                bool itemList = Editor.EmitChoiceIds.Count > 10 && Editor.EmitChoiceIds[0].Length == 0
                    && Editor.EmitChoiceIds.Contains("stackable_box") && Editor.EmitChoiceIds.Contains("product_lid_blue")
                    && !Editor.EmitChoiceIds.Contains("pallet");
                Editor.SetSelectedEmits("stackable_box");
                bool chosen = Editor.SelectedEmits == "stackable_box" && Editor.EmitChoiceIds.Count > 10;
                bool emitUndone = Editor.Undo() && Editor.SelectedEmits is null;
                bool emitRedone = Editor.Redo() && Editor.SelectedEmits == "stackable_box";
                SortersCheck("emitter item list, choice and undo", itemList && chosen && emitUndone && emitRedone);
                Editor.Place("palletizer", 0.0f, -3.0f);
                bool stationFields = Editor.ParamFieldNames.Contains("tableSpeed") && !Editor.ParamFieldNames.Contains("deck");
                Editor.Place("electric_switchboard", 3.5f, -3.0f);                 // visual only: no parameters
                SortersCheck("parameters per asset", emitterFields && stationFields && Editor.ParamFieldNames.Count == 0);

                // A conveyor placed higher stands on a support frame; on the floor it has none.
                SceneItem? high = Editor.Place("belt_conveyor_2m", 3.0f, 3.0f);
                int before = high is null ? -1 : Main.View.NodeOf(high.Id)!.GetChildren().Count(child => child.IsInGroup("supports"));
                Editor.SetSelectedHeight(1.9f);
                int after = high is null ? -1 : Main.View.NodeOf(high.Id)!.GetChildren().Count(child => child.IsInGroup("supports"));
                GD.Print($"SELFTEST ui: support beams on the floor={before}, raised={after}");
                SortersCheck("support frame under a raised conveyor", before == 0 && after == 8);
                Editor.Select(belt.Id);

                Main.Camera.SetView(0.0f, -89.0f);
                Main.FrameScene();
                bool top = Mathf.Abs(Main.Camera.PitchDegrees + 89.0f) < 0.1f && Main.Camera.GlobalPosition.Y > 5.0f;
                Main.Camera.SetView(-35.0f, -32.0f);
                Main.FocusOn(belt.Id);
                // The camera aims at the middle of the free part of the window, not of the window.
                Vector2 seen = Main.Camera.UnprojectPosition(Main.Camera.Pivot);
                GD.Print($"SELFTEST ui: pivot drawn at {seen}, free centre {free.GetCenter()}");
                SortersCheck("camera views and centring", top && seen.DistanceTo(free.GetCenter()) < 6.0f);
                Editor.Select(high?.Id);
                Main.FrameScene();
                _controlsPhase = 4;
                break;
            }

            case 4 when _wallClock >= 2.5:
                SaveScreenshot(suffix: "_design");
                Editor.SetDesignMode(false);
                SortersCheck("back to simulate", !Editor.DesignMode && hud.Groups.Contains("Palletizer")
                    && hud.IoRowCounts.Inputs + hud.IoRowCounts.Outputs == Main.TagMap.Entries.Count);
                RunUiPlan05();
                RunUiSettings();
                RunUiSigns();
                RunUiBranding();
                RunUiResources();
                RunUiAbout();
                RunUiWelcome();
                RunUiHelpMenu();
                RunUiCrash();
                RunUiTestPlcFit();
                _controlsPhase = 5;
                break;

            case 5 when _wallClock >= 3.5:
                SaveScreenshot(suffix: "_signs");
                CheckUiAboutTitle();
                CheckUiCrash();
                _controlsPhase = 6;
                break;

            case 6:
                if (!RunUiToolbarFit())
                {
                    break;
                }

                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// Plan 05, through the same operations as the buttons and the mouse: overlap markers, a group of items as one
    /// selection (move, turn, duplicate, delete: each one step for undo) and the size of the floor.
    /// </summary>
    private void RunUiPlan05()
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();

        // Two belts through each other are marked, one moved away is not.
        SceneItem? first = Editor.Place("belt_conveyor_2m", 0.0f, 0.0f);
        SceneItem? second = Editor.Place("belt_conveyor_2m", 0.5f, 0.0f);
        bool twoMarks = Editor.OverlapMarkerCount == 2;
        Editor.MoveSelected(2.0f, 0.0f);
        bool noMarks = Editor.OverlapMarkerCount == 0;
        Editor.SetDesignMode(false);
        bool hidden = Editor.OverlapMarkerCount == 0;
        Editor.SetDesignMode(true);
        SortersCheck("overlap markers", first is not null && second is not null && twoMarks && noMarks && hidden);

        // A group: three items in a row selected together.
        SceneItem? third = Editor.Place("belt_conveyor_2m", 4.0f, 0.0f);
        string[] ids = { first!.Id, second!.Id, third!.Id };
        Editor.SelectMany(ids);
        bool selected = Editor.SelectedIds.Count == 3 && Editor.ParamFieldNames.Count == 0;
        (double X, double Z)[] Positions() => ids.Select(id => (Main.Scene.Find(id)!.X, Main.Scene.Find(id)!.Z)).ToArray();
        (double X, double Z)[] start = Positions();

        Editor.MoveSelectedBy(0.0f, 2.0f);
        bool moved = Positions().All(position => position.Z == 2.0) && Positions().Select(position => position.X).SequenceEqual(start.Select(position => position.X));
        bool movedBack = Editor.Undo() && Positions().SequenceEqual(start);

        Editor.RotateSelected(90.0f);
        bool turned = ids.All(id => Main.Scene.Find(id)!.Yaw == 90.0) && Positions().Select(position => position.X).Distinct().Count() == 1;
        bool turnedBack = Editor.Undo() && Positions().SequenceEqual(start) && ids.All(id => Main.Scene.Find(id)!.Yaw == 0.0);

        int before = Main.Scene.Items.Count;
        Editor.DuplicateSelected();
        bool duplicated = Main.Scene.Items.Count == before + 3 && Editor.SelectedIds.Count == 3 && !Editor.SelectedIds.Intersect(ids).Any()
            && Main.Scene.Items.Select(item => item.Name).Distinct().Count() == Main.Scene.Items.Count;
        bool duplicatedBack = Editor.Undo() && Main.Scene.Items.Count == before;

        Editor.SelectMany(ids);
        Editor.CopySelected();
        Editor.PasteAt(0.0f, 6.0f);
        bool pasted = Main.Scene.Items.Count == before + 3 && Editor.SelectedIds.Count == 3
            && Editor.SelectedIds.All(id => Math.Abs(Main.Scene.Find(id)!.Z - 6.0) < 0.3);
        bool pastedBack = Editor.Undo() && Main.Scene.Items.Count == before;

        Editor.SelectMany(ids);
        Editor.DeleteSelected();
        bool deleted = Main.Scene.Items.Count == before - 3 && Editor.SelectedIds.Count == 0;
        bool deletedBack = Editor.Undo() && Main.Scene.Items.Count == before;
        SortersCheck("groups: select, move, turn, duplicate, copy and delete, each one step", selected && moved && movedBack && turned && turnedBack
            && duplicated && duplicatedBack && pasted && pastedBack && deleted && deletedBack);

        Editor.Select(first.Id);
        Editor.ToggleSelect(second.Id);
        bool two = Editor.SelectedIds.Count == 2 && Editor.SelectedId == second.Id;
        Editor.ToggleSelect(second.Id);
        bool one = Editor.SelectedIds.Count == 1 && Editor.SelectedId == first.Id;
        Editor.SelectAll();
        SortersCheck("shift-click adds and removes, select all", two && one && Editor.SelectedIds.Count == Main.Scene.Items.Count);

        // The floor: another size, one step for undo, and nonsense refused.
        bool resized = Editor.SetFloor(20.0, 12.0) is null && Main.FloorSize == (20.0f, 12.0f) && Main.Scene.Floor.Length == 20.0;
        bool undone = Editor.Undo() && Main.FloorSize == (14.0f, 10.0f);
        bool redone = Editor.Redo() && Main.FloorSize == (20.0f, 12.0f);
        bool refused = Editor.SetFloor(2.0, 2.0) is not null && Main.FloorSize == (20.0f, 12.0f);
        Main.FrameScene();
        SaveScreenshot(suffix: "_floor");
        SortersCheck("floor size, undo and refusal", resized && undone && redone && refused);
        GD.Print($"SELFTEST ui plan 05: floor now {Main.FloorSize}");

        // Pointing at a tag: only the tags of one part, a click on a part while simulating does the same and lights it up,
        // a click on empty floor shows everything again, and the I/O list goes to a GVL file.
        Editor.SetDesignMode(false);
        Ui.Hud hud = Main.Hud;
        int allGroups = hud.Groups.Count;
        hud.ShowOnly(second.Id);
        bool filtered = hud.ItemFilterId == second.Id && hud.Groups.Count == 1 && hud.Groups[0] == second.Name
            && hud.IoRowCounts.Inputs + hud.IoRowCounts.Outputs == 1;
        hud.ShowOnly(null);
        bool unfiltered = hud.ItemFilterId is null && hud.Groups.Count == allGroups;
        Editor.FlashItem(second.Id);
        bool flashing = Editor.FlashId == second.Id;
        Main.FrameScene();
        Main.InspectAt(Main.Camera.UnprojectPosition(new Vector3(2.0f, 0.0f, 0.0f)));
        bool byClick = hud.ItemFilterId == second.Id && Editor.FlashId == second.Id && hud.Groups.Count == 1;
        Main.InspectAt(Main.Camera.UnprojectPosition(new Vector3(-6.0f, 0.0f, 4.0f)));
        bool byEmptyClick = hud.ItemFilterId is null && hud.Groups.Count == allGroups;
        string? gvl = hud.ExportGvl();
        string gvlText = gvl is null ? string.Empty : System.IO.File.ReadAllText(gvl);
        bool exported = gvlText.Contains("VAR_GLOBAL", StringComparison.Ordinal) && gvlText.Contains("oConveyor2", StringComparison.Ordinal)
            && gvlText.Contains("iSimRunning", StringComparison.Ordinal);
        SortersCheck("tag pointing: filter, flash, click and GVL export", filtered && unfiltered && flashing && byClick && byEmptyClick && exported);
    }

    /// <summary>
    /// The PLC program for the "palletizer" test, on tags only. Bring a pallet onto the table,
    /// raise it under the plate, form a row of three, push it over, form a second row, push again
    /// and keep pushing while the plate opens, lower the table until the stack is clear, close the
    /// plate and retract. After two layers: table down and roll the pallet out.
    /// </summary>
    private void RunPalletizerPlc()
    {
        if (!Main.Tags.SensorNames.Contains("Palletizer row full"))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        double inStep = _elapsed - _stationSince;
        bool rowFull = Sensor(sensors, "Palletizer row full");
        if (!rowFull)
        {
            _rowFullSince = _elapsed;
        }

        var on = new System.Collections.Generic.HashSet<string> { "Conveyor 2" };
        int next = _stationStep;
        switch (_stationStep)
        {
            case 0:     // pallet in
                on.UnionWith(new[] { "Emitter", "Conveyor", "Palletizer rollers" });
                next = Sensor(sensors, "Palletizer pallet") ? 1 : 0;
                break;
            case 1:     // table up until the pallet is just under the plate
                on.Add("Palletizer up");
                next = Sensor(sensors, "Palletizer stack high") ? 2 : 1;
                break;
            case 2:     // form a row; the last place must stay occupied, a passing box does not count
                on.UnionWith(new[] { "Emitter 2", "Conveyor 3", "Palletizer feed" });
                next = rowFull && _elapsed - _rowFullSince >= 1.2 ? 3 : 2;
                break;
            case 3:     // push the row onto the plate
                on.Add("Palletizer push");
                if (Sensor(sensors, "Palletizer pusher front"))
                {
                    _palletRows++;
                    next = _palletRows < 2 ? 4 : 5;
                }

                break;
            case 4:     // retract for the second row
                next = Sensor(sensors, "Palletizer pusher back") ? 2 : 4;
                break;
            case 5:     // open the plate, the pusher keeps the layer in place
                on.UnionWith(new[] { "Palletizer push", "Palletizer open" });
                next = Sensor(sensors, "Palletizer plate open") && inStep >= 1.0 ? 6 : 5;
                break;
            case 6:     // lower the table until the stack is below the plate
                on.UnionWith(new[] { "Palletizer push", "Palletizer open", "Palletizer down" });
                next = !Sensor(sensors, "Palletizer stack high") ? 7 : 6;
                break;
            case 7:     // close the plate
                on.Add("Palletizer push");
                next = Sensor(sensors, "Palletizer plate closed") ? 8 : 7;
                break;
            case 8:     // retract
                if (Sensor(sensors, "Palletizer pusher back"))
                {
                    _palletRows = 0;
                    _palletLayers++;
                    next = _palletLayers < 2 ? 2 : 9;
                }

                break;
            case 9:     // table down
                on.Add("Palletizer down");
                next = Sensor(sensors, "Palletizer table down") ? 10 : 9;
                break;
            case 10:    // pallet out
                on.Add("Palletizer rollers");
                if (inStep >= 6.0)
                {
                    _palletLayers = 0;
                    _stationCycles++;
                    next = 11;
                }

                break;
            case 11:    // one pallet is enough for the test
                break;
        }

        if (next != _stationStep)
        {
            GD.Print($"SELFTEST palletizer plc: step {_stationStep} -> {next} at {_elapsed:0.0} s");
            _stationStep = next;
            _stationSince = _elapsed;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// A palletizer built with the editor: pallets arrive on a roller conveyor, boxes on a belt at
    /// plate height. Under the PLC program above two layers of six boxes must end up on the pallet
    /// and the loaded pallet must arrive at the remover.
    /// </summary>
    private void RunPalletizer(double delta)
    {
        _wallClock += delta;
        Remover? remover = Main.View.Find<Remover>("Remover");
        Palletizer? station = Main.View.Find<Palletizer>("Palletizer");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("roller_conveyor_2m", -2.25f, 0.0f) is not null
                    && Editor.Place("emitter_pallet", -2.5f, 0.0f) is not null
                    && Editor.Place("palletizer", 0.0f, 0.0f) is not null
                    && Editor.Place("roller_conveyor_2m", 2.25f, 0.0f) is not null;
                SceneItem? hood = Editor.Place("remover_hood_1m4", 3.0f, 0.0f);
                Editor.SetSelectedHeight(0.5f);
                SceneItem? belt = Editor.Place("belt_conveyor_2m", -2.25f, -0.5f);
                Editor.SetSelectedHeight(1.9f);
                SceneItem? boxes = Editor.Place("emitter_gantry", -2.75f, -0.5f);
                Editor.SetSelectedHeight(1.9f);
                Editor.SetSelectedParam("highRatio", 0.0);
                Editor.SetDesignMode(false);
                SortersCheck("built", built && hood is not null && belt is not null && boxes is not null
                    && new[] { "Emitter", "Emitter 2", "Conveyor", "Conveyor 2", "Conveyor 3", "Palletizer feed", "Palletizer push",
                        "Palletizer open", "Palletizer up", "Palletizer down", "Palletizer rollers" }.All(Main.Tags.ActuatorNames.Contains)
                    && new[] { "Palletizer row full", "Palletizer pusher back", "Palletizer pusher front", "Palletizer plate open",
                        "Palletizer plate closed", "Palletizer stack high", "Palletizer table down", "Palletizer pallet" }
                        .All(Main.Tags.SensorNames.Contains));
                _stationStep = 0;
                _stationCycles = 0;
                _palletRows = 0;
                _palletLayers = 0;
                Main.Camera.Frame(new Vector3(0.0f, 1.3f, 0.0f), 9.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 1.0:
                SortersCheck("at rest", station is { PusherBack: true, PlateClosed: true, TableDown: true, PalletPresent: false });
                _controlsPhase = 2;
                break;

            case 2 when _stationStep == 6:
                SaveScreenshot(suffix: "_layer");
                _controlsPhase = 3;
                break;

            case 3 when _stationStep >= 9:
            {
                // Both layers are down: twelve boxes must sit on the pallet, inside its outline.
                Box[] all = GetTree().GetNodesInGroup(Box.Group).OfType<Box>().Where(box => !box.IsQueuedForDeletion()).ToArray();
                Box? pallet = all.FirstOrDefault(box => !box.IsStandardBox);
                int onPallet = pallet is null ? 0 : all.Count(box => box.IsStandardBox
                    && Mathf.Abs(box.GlobalPosition.X - pallet.GlobalPosition.X) < 0.6f
                    && Mathf.Abs(box.GlobalPosition.Z - pallet.GlobalPosition.Z) < 0.45f
                    && box.GlobalPosition.Y > pallet.GlobalPosition.Y && box.GlobalPosition.Y < 1.85f);
                int upright = all.Count(box => box.GlobalBasis.Y.Dot(Vector3.Up) > 0.98f);
                GD.Print($"SELFTEST palletizer, two layers: on pallet={onPallet} upright={upright}/{all.Length} at {_elapsed:0.0} s");
                SortersCheck("two layers on the pallet", onPallet == 12 && upright == all.Length);
                SaveScreenshot(suffix: "_stack");
                _controlsPhase = 4;
                break;
            }

            case 4 when _stationStep == 11 && _elapsed - _stationSince >= 6.0 || _wallClock >= 240.0:
                GD.Print($"SELFTEST palletizer: step={_stationStep} pallets out={_stationCycles} removed={remover?.Count} "
                    + $"(low={remover?.LowCount} other={remover?.OtherCount}) fallen={fallen} at {_elapsed:0.0} s");
                SortersCheck("loaded pallet delivered", remover is { LowCount: 12, HighCount: 0, OtherCount: 1 } && fallen == 0);
                Main.Sim.Reset();
                _wallClock = 1000.0;
                _controlsPhase = 5;
                break;

            case 2 or 3 when _wallClock >= 240.0:
                GD.Print($"SELFTEST palletizer: stuck in step {_stationStep} (rows={_palletRows} layers={_palletLayers}) fallen={fallen}");
                SortersCheck("sequence finished in time", false);
                _controlsPhase = 4;
                break;

            case 5 when _wallClock >= 1001.0:
                SortersCheck("reset", station is { PusherBack: true, PlateClosed: true, TableDown: true } && Main.CountBoxes().InScene == 0);
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// The PLC program for the "machining" test, on tags only. Feed until a part lies at the
    /// entry, give the start (with the lid output on for every second part), wait until the
    /// station is no longer busy. During the second cycle the exit conveyor stands still, so
    /// the first product blocks the exit and the station has to wait with the second one.
    /// </summary>
    private void RunMachiningPlc()
    {
        const string name = "Machining center";
        if (!Main.Tags.SensorNames.Contains(name + " busy"))
        {
            return;
        }

        // What comes out, seen from outside the PLC: every base and lid that ever existed.
        foreach (Node node in GetTree().GetNodesInGroup(Box.Group))
        {
            if (node is Box { ItemModel: { } model } box)
            {
                if (model.StartsWith("product_base_", StringComparison.Ordinal))
                {
                    _machBases.Add(box.GetInstanceId());
                }
                else if (model.StartsWith("product_lid_", StringComparison.Ordinal))
                {
                    _machLids.Add(box.GetInstanceId());
                }
            }
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        bool busy = Sensor(sensors, name + " busy");
        bool entry = Sensor(sensors, name + " entry");
        bool exit = Sensor(sensors, name + " exit");
        MachiningCenter? station = Main.View.Find<MachiningCenter>(name);

        var on = new System.Collections.Generic.HashSet<string>();
        bool holdExit = _stationCycles == 1 && !_machWaited;
        if (holdExit && station is { Step: FactorySim.Core.MachiningStep.ToExit } && exit)
        {
            _machWaitedSince = _machWaitedSince < 0.0 ? _elapsed : _machWaitedSince;
            _machWaited = _elapsed - _machWaitedSince >= 3.0;
        }

        if (!holdExit)
        {
            on.Add("Conveyor 2");
        }

        if (_stationCycles % 2 == 1)
        {
            on.Add(name + " lid");
        }

        int next = _stationStep;
        switch (_stationStep)
        {
            case 0:     // feed until a part lies against the stop
                if (!entry)
                {
                    on.UnionWith(new[] { "Emitter", "Conveyor" });
                }

                next = entry && _elapsed - _stationSince >= 0.5 ? 1 : 0;
                break;
            case 1:     // start
                on.Add(name + " start");
                next = busy ? 2 : 1;
                break;
            case 2:     // wait for the station
                if (!busy)
                {
                    _stationCycles++;
                    next = _stationCycles < 4 ? 0 : 3;
                }

                break;
            case 3:     // four parts are enough for the test
                break;
        }

        if (next != _stationStep)
        {
            GD.Print($"SELFTEST machining plc: step {_stationStep} -> {next} at {_elapsed:0.0} s");
            _stationStep = next;
            _stationSince = _elapsed;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// A machining center built with the editor: raw material arrives on a belt, products leave
    /// on a second belt to a remover. Under the PLC program above four parts must come out, two
    /// bases and two lids, and the station must wait while its exit is blocked.
    /// </summary>
    private void RunMachining(double delta)
    {
        _wallClock += delta;
        const string name = "Machining center";
        Remover? remover = Main.View.Find<Remover>("Remover");
        MachiningCenter? station = Main.View.Find<MachiningCenter>(name);
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("machining_center", 0.0f, 0.0f) is not null
                    && Editor.Place("belt_conveyor_2m", -2.5f, 0.6f) is not null
                    && Editor.Place("emitter_raw_blue", -3.2f, 0.6f) is not null
                    && Editor.Place("belt_conveyor_2m", -2.5f, -0.6f, 180.0f) is not null
                    && Editor.Place("remover_hood_1m0", -3.25f, -0.6f, 180.0f) is not null;
                Editor.SetDesignMode(false);
                SortersCheck("built", built
                    && new[] { "Emitter", "Conveyor", "Conveyor 2", name + " start", name + " lid" }.All(Main.Tags.ActuatorNames.Contains)
                    && new[] { name + " busy", name + " entry", name + " exit" }.All(Main.Tags.SensorNames.Contains));
                _stationStep = 0;
                _stationCycles = 0;
                Main.Camera.Frame(new Vector3(-0.6f, 0.9f, 0.0f), 5.0f);
                Main.Camera.SetView(-125.0f, -38.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 0.5:
                SortersCheck("at rest", station is { Busy: false, EntryPresent: false, ExitOccupied: false, Finished: 0 });
                _controlsPhase = 2;
                break;

            case 2 when station is { Step: FactorySim.Core.MachiningStep.Machining } && _stationCycles == 0:
                _controlsPhase = 3;
                _stationSince = _elapsed;
                break;

            case 3 when _elapsed - _stationSince >= 1.5:
                SaveScreenshot(suffix: "_machining");
                _controlsPhase = 4;
                break;

            case 4 when _machWaitedSince >= 0.0 && _elapsed - _machWaitedSince >= 2.0:
                SortersCheck("waits while the exit is blocked",
                    station is { Step: FactorySim.Core.MachiningStep.ToExit, Busy: true, ExitOccupied: true, Finished: 1 });
                SaveScreenshot(suffix: "_machining_waiting");
                _controlsPhase = 5;
                break;

            case 5 when _stationStep == 3 && _elapsed - _stationSince >= 5.0 || _wallClock >= 150.0:
                GD.Print($"SELFTEST machining: cycles={_stationCycles} finished={station?.Finished} bases={_machBases.Count} "
                    + $"lids={_machLids.Count} removed={remover?.Count} (other={remover?.OtherCount}) fallen={fallen} at {_elapsed:0.0} s");
                SortersCheck("four parts machined", station is { Finished: 4, Busy: false } && _machBases.Count == 2 && _machLids.Count == 2);
                SortersCheck("products delivered", remover is { OtherCount: 4, LowCount: 0, HighCount: 0 } && fallen == 0);
                Main.Sim.Reset();
                _wallClock = 1000.0;
                _controlsPhase = 6;
                break;

            case 2 or 3 or 4 when _wallClock >= 150.0:
                GD.Print($"SELFTEST machining: stuck in plc step {_stationStep}, station step {station?.Step}, cycles={_stationCycles}");
                SortersCheck("sequence finished in time", false);
                _controlsPhase = 5;
                break;

            case 6 when _wallClock >= 1001.0:
                SortersCheck("reset", station is { Busy: false, Finished: 0 } && Main.CountBoxes().InScene == 0);
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>One step of the PLC program of the "crane" test: tags that are on, and what to wait for.</summary>
    private sealed record CraneStep(string Label, string[] On, string[] WaitFor, string[] WaitNot, double MinTime, Func<bool[], bool>? Verify = null);

    private const string CraneName = "Stacker crane";

    private static string CraneTag(string suffix) => CraneName + " " + suffix;

    private static string[] CraneBits(int number) =>
        new[] { 1, 2, 4, 8 }.Where(weight => (number & weight) != 0).Select(weight => CraneTag("target " + weight)).ToArray();

    private static CraneStep CraneGo(int number, params string[] more) =>
        new($"go {number}", CraneBits(number).Concat(more).Append(CraneTag("go")).ToArray(), Array.Empty<string>(), Array.Empty<string>(), 0.3);

    private static CraneStep CraneArrive(int number, params string[] more) =>
        new($"at {number}", CraneBits(number).Concat(more).ToArray(), new[] { CraneTag("at target") }, new[] { CraneTag("busy") }, 0.0);

    private static CraneStep CraneForksOut(string side, params string[] more) =>
        new($"forks {side} out", more.Append(CraneTag("forks " + side)).ToArray(), new[] { CraneTag("forks " + side + " end") },
            Array.Empty<string>(), 0.0);

    private static CraneStep CraneForksBack(Func<bool[], bool>? verify, params string[] more) =>
        new("forks back", more, new[] { CraneTag("forks middle") }, Array.Empty<string>(), 0.0, verify);

    private static Vector3 CraneSlot(int number)
    {
        int column = (number - 1) % 3 + 1;
        int scene = (number - 1) / 3;
        return new Vector3((column - 2) * 1.5f, 0.6f + scene * 1.2f, -1.5f);
    }

    private Box[] Pallets() => GetTree().GetNodesInGroup(Box.Group).OfType<Box>()
        .Where(box => !box.IsQueuedForDeletion() && box.ItemModel == "pallet").ToArray();

    /// <summary>A pallet rests upright with its underside on the given point (top of the support), within 5 cm.</summary>
    private bool PalletOn(Vector3 support) => Pallets().Any(box =>
        (box.GlobalPosition - (support + Vector3.Up * (box.Height / 2.0f))).Length() < 0.05f
        && box.GlobalBasis.Y.Dot(Vector3.Up) > 0.98f);

    private bool CraneStandsAt(double x, double height)
    {
        StackerStation? crane = Main.View.Find<StackerStation>(CraneName);
        return crane is not null && Math.Abs(crane.Logic.X - x) < 0.025 && Math.Abs(crane.Logic.PlatformHeight - height) < 0.025;
    }

    /// <summary>The PLC program for the "crane" test: put pallets into slots 1, 5 and 12, take slot 5 out, then provoke errors.</summary>
    private System.Collections.Generic.List<CraneStep> BuildCranePlan()
    {
        string lift = CraneTag("lift");
        string right = CraneTag("forks right");
        string left = CraneTag("forks left");
        var plan = new System.Collections.Generic.List<CraneStep>
        {
            new("settle", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 1.0),
        };

        foreach (int slot in new[] { 1, 5, 12 })
        {
            int target = slot;
            plan.Add(new CraneStep("pallet arrives", new[] { "Emitter" }, Array.Empty<string>(), Array.Empty<string>(), 2.5));
            plan.Add(CraneGo(0));
            plan.Add(CraneArrive(0));
            plan.Add(CraneForksOut("right"));
            plan.Add(new CraneStep("lift the pallet off the rollers", new[] { right, lift }, new[] { CraneTag("lifted") }, Array.Empty<string>(), 0.0));
            plan.Add(CraneForksBack(sensors => Sensor(sensors, CraneTag("load")), lift));
            plan.Add(CraneGo(target, lift));
            plan.Add(CraneArrive(target, lift));
            plan.Add(CraneForksOut("left", lift));
            if (slot == 5)
            {
                plan.Add(new CraneStep("pallet in the rack", new[] { left, lift }, Array.Empty<string>(), Array.Empty<string>(), 0.5,
                    _ =>
                    {
                        SaveScreenshot(suffix: "_crane");
                        return true;
                    }));
            }

            plan.Add(new CraneStep("set the pallet down", new[] { left }, Array.Empty<string>(), new[] { CraneTag("lifted") }, 1.3));
            plan.Add(CraneForksBack(sensors => !Sensor(sensors, CraneTag("load")) && PalletOn(CraneSlot(target))));
        }

        plan.Add(new CraneStep("pallets in slots 1, 5 and 12", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 1.0,
            _ => Pallets().Length == 3 && PalletOn(CraneSlot(1)) && PalletOn(CraneSlot(5)) && PalletOn(CraneSlot(12))));

        // Take slot 5 out onto the conveyor at the out position.
        plan.Add(CraneGo(5));
        plan.Add(CraneArrive(5));
        plan.Add(CraneForksOut("left"));
        plan.Add(new CraneStep("lift the pallet out of the slot", new[] { left, lift }, new[] { CraneTag("lifted") }, Array.Empty<string>(), 0.0));
        plan.Add(CraneForksBack(sensors => Sensor(sensors, CraneTag("load")), lift));
        plan.Add(CraneGo(13, lift));
        plan.Add(CraneArrive(13, lift));
        plan.Add(CraneForksOut("right", lift));
        plan.Add(new CraneStep("set the pallet down on the out conveyor", new[] { right }, Array.Empty<string>(), new[] { CraneTag("lifted") }, 1.3));
        plan.Add(CraneForksBack(sensors => !Sensor(sensors, CraneTag("load"))));
        // The out conveyor runs away from the crane to a remover: it must count exactly one pallet.
        plan.Add(new CraneStep("slot 5 is out", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 9.0,
            _ => Main.View.Find<Remover>("Remover") is { OtherCount: 1, Count: 1 } && Pallets().Length == 2
                && PalletOn(CraneSlot(1)) && PalletOn(CraneSlot(12))));

        // Errors: a slot that does not exist, and a go with the forks out. The crane must stay where it is.
        plan.Add(new CraneStep("go 15", CraneBits(15).Append(CraneTag("go")).ToArray(), Array.Empty<string>(), Array.Empty<string>(), 0.6,
            sensors => Sensor(sensors, CraneTag("error")) && !Sensor(sensors, CraneTag("busy")) && CraneStandsAt(1.5, 0.47)));
        plan.Add(CraneForksOut("right"));
        plan.Add(new CraneStep("go with the forks out", CraneBits(2).Append(CraneTag("go")).Append(right).ToArray(), Array.Empty<string>(),
            Array.Empty<string>(), 0.6,
            sensors => Sensor(sensors, CraneTag("error")) && !Sensor(sensors, CraneTag("busy")) && CraneStandsAt(1.5, 0.47)));
        plan.Add(CraneForksBack(null));
        return plan;
    }

    /// <summary>The PLC program for the jog mode: to column 2 and scene 3 on the sensors, and no driving with the forks out.</summary>
    private System.Collections.Generic.List<CraneStep> BuildCraneJogPlan()
    {
        return new System.Collections.Generic.List<CraneStep>
        {
            new("settle", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 0.5,
                sensors => Sensor(sensors, CraneTag("at column 1")) && Sensor(sensors, CraneTag("at level 0"))),
            new("right to column 2", new[] { CraneTag("right") }, new[] { CraneTag("at column 2") }, Array.Empty<string>(), 0.0),
            new("up to level 3", new[] { CraneTag("up") }, new[] { CraneTag("at level 3") }, Array.Empty<string>(), 0.0),
            new("stands on column 2, level 3", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 1.0,
                sensors => Sensor(sensors, CraneTag("at column 2")) && Sensor(sensors, CraneTag("at level 3"))
                    && !Sensor(sensors, CraneTag("at column 1")) && CraneStandsAt(0.0, 2.97)),
            CraneForksOut("left"),
            new("no driving with the forks out", new[] { CraneTag("right"), CraneTag("up"), CraneTag("forks left") },
                Array.Empty<string>(), Array.Empty<string>(), 1.5, _ => CraneStandsAt(0.0, 2.97)),
            CraneForksBack(null),
            new("left to column 1", new[] { CraneTag("left") }, new[] { CraneTag("at column 1") }, Array.Empty<string>(), 0.0),
            new("down to level 1", new[] { CraneTag("down") }, new[] { CraneTag("at level 1") }, Array.Empty<string>(), 0.0),
            new("stands on column 1, level 1", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 0.5,
                _ => CraneStandsAt(-1.5, 0.57)),
        };
    }

    private void RunCranePlc()
    {
        if (_cranePlan is null || !Main.Tags.SensorNames.Contains(CraneTag("forks middle")))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        CraneStep? step = _craneIndex < _cranePlan.Count ? _cranePlan[_craneIndex] : null;
        while (step is not null && _elapsed - _craneStepSince >= step.MinTime
            && step.WaitFor.All(name => Sensor(sensors, name)) && step.WaitNot.All(name => !Sensor(sensors, name)))
        {
            if (step.Verify is { } verify)
            {
                bool ok = verify(sensors);
                GD.Print($"SELFTEST crane plc: {step.Label}: {(ok ? "ok" : "WRONG")} at {_elapsed:0.0} s");
                if (!ok)
                {
                    StackerStation? where = Main.View.Find<StackerStation>(CraneName);
                    GD.Print($"SELFTEST crane state: x={where?.Logic.X:0.000} h={where?.Logic.PlatformHeight:0.000} fork={where?.Logic.ForkOffset:0.000} "
                        + $"lift={where?.Logic.LiftOffset:0.000} holding={where?.Holding} pallets: "
                        + string.Join("; ", Pallets().Select(box => box.GlobalPosition.ToString("0.00") + (box.Freeze ? " frozen" : string.Empty))));
                }

                SortersCheck(step.Label, ok);
            }

            _craneIndex++;
            _craneStepSince = _elapsed;
            step = _craneIndex < _cranePlan.Count ? _cranePlan[_craneIndex] : null;
        }

        var on = new System.Collections.Generic.HashSet<string>(step?.On ?? Array.Empty<string>());
        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// A stacker crane with a rack and two roller conveyors, built with the editor. Under the PLC
    /// program above three pallets go into slots 1, 5 and 12, slot 5 comes out again onto the out
    /// conveyor, and a go to a slot that does not exist or with the forks out is refused. Then the
    /// same crane in jog mode drives to a column and a level on the position sensors.
    /// </summary>
    private void RunCrane(double delta)
    {
        _wallClock += delta;
        StackerStation? crane = Main.View.Find<StackerStation>(CraneName);

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("stacker_crane", 0.0f, 0.0f) is not null;
                Editor.SetSelectedParam("driveSpeed", 1.0);
                Editor.SetSelectedParam("hoistSpeed", 0.8);
                built = built && Editor.Place("rack", 0.0f, -1.5f) is not null
                    && Editor.Place("roller_conveyor_2m", -1.5f, 2.0f, 90.0f) is not null
                    && Editor.Place("emitter_pallet", -1.5f, 1.5f, 90.0f) is not null;
                Editor.SetSelectedParam("interval", 1.0);
                // The in conveyor stands still; the out conveyor runs away from the crane to a remover.
                built = built && Editor.Place("roller_conveyor_2m", 1.5f, 2.0f, 270.0f) is not null;
                Editor.SetSelectedParam(SceneTags.AlwaysOn, 1.0);
                built = built && Editor.Place("remover_hood_1m4", 1.5f, 2.75f, 270.0f) is not null;
                Editor.SetDesignMode(false);
                string[] actuators = { "forks left", "forks right", "lift", "target 1", "target 2", "target 4", "target 8", "go" };
                string[] sensors = { "forks middle", "forks left end", "forks right end", "lifted", "load", "busy", "at target", "error" };
                SortersCheck("built", built
                    && actuators.All(suffix => Main.Tags.ActuatorNames.Contains(CraneTag(suffix)))
                    && sensors.All(suffix => Main.Tags.SensorNames.Contains(CraneTag(suffix)))
                    && Main.Tags.ActuatorNames.Contains("Emitter"));
                _cranePlan = BuildCranePlan();
                _craneIndex = 0;
                _craneStepSince = 0.0;
                Main.Camera.Frame(new Vector3(0.0f, 2.2f, -0.3f), 12.0f);
                Main.Camera.SetView(-60.0f, -22.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 0.5:
                SortersCheck("at rest", crane is { Load: false, Holding: false } && !crane.Logic.Busy && crane.Logic.ForksMiddle
                    && CraneStandsAt(-1.5, 0.47));
                _controlsPhase = 2;
                break;

            case 2 when _cranePlan is not null && _craneIndex >= _cranePlan.Count:
                GD.Print($"SELFTEST crane: plan finished, pallets={Pallets().Length} at {_elapsed:0.0} s");
                SortersCheck("nothing fell", Pallets().Length == 2 && Pallets().All(box => box.GlobalPosition.Y > 0.5f));
                Main.Sim.Reset();
                _wallClock = 1000.0;
                _controlsPhase = 3;
                break;

            case 2 when _wallClock >= 500.0:
                GD.Print($"SELFTEST crane: stuck in step {_craneIndex} ({_cranePlan?[Math.Min(_craneIndex, _cranePlan.Count - 1)].Label})");
                SortersCheck("sequence finished in time", false);
                _controlsPhase = 3;
                _wallClock = 1000.0;
                break;

            case 3 when _wallClock >= 1001.0:
                SortersCheck("reset", crane is { Load: false, Holding: false } && !crane.Logic.Busy && !crane.Logic.Error
                    && CraneStandsAt(-1.5, 0.47) && Main.CountBoxes().InScene == 0);
                _controlsPhase = 4;
                break;

            case 4:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("stacker_crane", 0.0f, 0.0f) is not null;
                Editor.SetSelectedParam(SceneTags.StackerJog, 1.0);
                Editor.SetSelectedParam("driveSpeed", 1.0);
                Editor.SetSelectedParam("hoistSpeed", 0.8);
                Editor.SetDesignMode(false);
                string[] jogActuators = { "left", "right", "up", "down", "forks left", "forks right", "lift" };
                string[] jogSensors = { "at column 1", "at column 2", "at column 3", "at level 0", "at level 1", "at level 2", "at level 3", "at level 4" };
                SortersCheck("jog mode tags", built
                    && jogActuators.All(suffix => Main.Tags.ActuatorNames.Contains(CraneTag(suffix)))
                    && jogSensors.All(suffix => Main.Tags.SensorNames.Contains(CraneTag(suffix)))
                    && !Main.Tags.ActuatorNames.Contains(CraneTag("go")));
                _cranePlan = BuildCraneJogPlan();
                _craneIndex = 0;
                _craneStepSince = _elapsed;
                _wallClock = 0.0;
                Main.Camera.Frame(new Vector3(0.0f, 2.2f, 0.0f), 10.0f);
                Main.Sim.Run();
                _controlsPhase = 5;
                break;
            }

            case 5 when _cranePlan is not null && _craneIndex >= _cranePlan.Count:
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;

            case 5 when _wallClock >= 120.0:
                GD.Print($"SELFTEST crane: jog plan stuck in step {_craneIndex}");
                SortersCheck("jog sequence finished in time", false);
                _reported = true;
                GD.Print("SELFTEST RESULT: FAIL");
                GetTree().Quit(1);
                break;
        }
    }

    /// <summary>
    /// The PLC program for the "lift" test, on tags only: feed a box onto the platform until the
    /// sensor sees it, go up, roll it off onto the upper conveyor, come down for the next one.
    /// </summary>
    private void RunLiftPlc()
    {
        if (!Main.Tags.SensorNames.Contains("Elevator at top"))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        double inStep = _elapsed - _stationSince;
        var on = new System.Collections.Generic.HashSet<string> { "Conveyor 2" };
        int next = _stationStep;
        switch (_stationStep)
        {
            case 0:     // feed until a box is on the platform
                on.UnionWith(new[] { "Conveyor", "Emitter", "Elevator" });
                next = Sensor(sensors, "Sensor") && Sensor(sensors, "Elevator at bottom") ? 1 : 0;
                break;
            case 1:     // up
                on.Add("Elevator up");
                next = Sensor(sensors, "Elevator at top") ? 2 : 1;
                break;
            case 2:     // roll the box off at the top
                on.UnionWith(new[] { "Elevator up", "Elevator" });
                next = inStep >= 3.5 ? 3 : 2;
                break;
            case 3:     // down
                next = Sensor(sensors, "Elevator at bottom") ? 0 : 3;
                if (next == 0)
                {
                    _stationCycles++;
                }

                break;
        }

        if (next != _stationStep)
        {
            _stationStep = next;
            _stationSince = _elapsed;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// A roller conveyor on the floor level, an elevator, and a roller conveyor with a remover
    /// 3 m higher, built with the editor. Under the PLC program above every box must arrive at
    /// the remover on the upper scene. A box that rolls across the platform while it is down, or
    /// off it on the way, drops to the floor and counts as fallen.
    /// </summary>
    private void RunLift(double delta)
    {
        _wallClock += delta;
        Remover? remover = Main.View.Find<Remover>("Remover");
        Elevator? lift = Main.View.Find<Elevator>("Elevator");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("roller_conveyor_2m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", -0.5f, 0.0f) is not null
                    && Editor.Place("elevator", 1.75f, 0.0f) is not null;
                SceneItem? upper = Editor.Place("roller_conveyor_2m", 3.5f, 0.0f);
                Editor.SetSelectedHeight((float)(upper?.Y ?? 0.5) + 3.0f);
                SceneItem? hood = Editor.Place("remover_hood_1m0", 4.75f, 0.0f);
                Editor.SetSelectedHeight((float)(hood?.Y ?? 0.8) + 3.0f);
                built &= upper is not null && hood is not null && Editor.Place("diffuse_sensor", 1.75f, 1.0f, 180.0f) is not null;
                Editor.SetSelectedHeight(0.6f);
                Editor.SetDesignMode(false);
                SortersCheck("built", built
                    && new[] { "Elevator", "Elevator reverse", "Elevator up" }.All(Main.Tags.ActuatorNames.Contains)
                    && new[] { "Elevator at bottom", "Elevator at top" }.All(Main.Tags.SensorNames.Contains));
                _stationStep = 0;
                _stationCycles = 0;
                Main.Camera.Frame(new Vector3(2.0f, 2.0f, 0.0f), 10.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 2.0:
                SortersCheck("at bottom", lift is { AtBottom: true, AtTop: false } && Mathf.Abs(lift.SurfaceHeight - 0.5f) < 0.001f);
                _controlsPhase = 2;
                break;

            case 2 when lift is { AtTop: true }:
                // First time at the top: the box must have come along.
                (int inScene, int _) = Main.CountBoxes();
                float highest = GetTree().GetNodesInGroup(Box.Group).OfType<Box>().Select(box => box.GlobalPosition.Y).DefaultIfEmpty(0.0f).Max();
                GD.Print($"SELFTEST lift, at the top: surface={lift.SurfaceHeight:0.00} highest box={highest:0.00} boxes={inScene}");
                // The limit switch closes within PusherLogic.LimitTolerance of the end of the travel.
                SortersCheck("box carried up", Mathf.Abs(lift.SurfaceHeight - 3.5f) <= (float)FactorySim.Core.PusherLogic.LimitTolerance + 0.001f && highest > 3.5f);
                SaveScreenshot(suffix: "_top");
                _controlsPhase = 3;
                break;

            case 3 when _wallClock >= 90.0:
                GD.Print($"SELFTEST lift: cycles={_stationCycles} removed={remover?.Count} fallen={fallen} "
                    + $"emitted={Main.View.Emitters.Sum(e => e.EmittedCount)}");
                SortersCheck("boxes delivered upstairs", remover is { Count: >= 3 } && fallen == 0 && _stationCycles >= 3);
                Main.Sim.Reset();
                _controlsPhase = 4;
                break;

            case 4 when _wallClock >= 91.0:
                SortersCheck("reset", lift is { AtBottom: true } && Main.CountBoxes().InScene == 0);
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// The PLC program for the "station" test, on tags only: feed a box until the sensor sees its
    /// front, let the belt run on 0.2 s so the box is centred under the gripper, then go down,
    /// grab, go up, travel over, go down, release, go up and travel back.
    /// </summary>
    private void RunStationPlc()
    {
        const string p = "Pick and place";
        if (!Main.Tags.SensorNames.Contains(p + " item"))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        bool movingZ = Sensor(sensors, p + " moving Z");
        bool movingXy = Sensor(sensors, p + " moving X") || (_stationHasY && Sensor(sensors, p + " moving Y"));
        bool item = Sensor(sensors, p + " item");
        double inStep = _elapsed - _stationSince;
        var on = new System.Collections.Generic.HashSet<string> { "Conveyor 2" };
        string[] over = _stationHasY ? new[] { p + " X", p + " Y" } : new[] { p + " X" };
        int next = _stationStep;

        switch (_stationStep)
        {
            case 0:     // feed until the sensor sees a box
                on.UnionWith(new[] { "Conveyor", "Emitter" });
                next = Sensor(sensors, "Sensor") ? 1 : 0;
                break;
            case 1:     // run on, so the box is under the gripper
                on.Add("Conveyor");
                next = inStep >= 0.2 ? 2 : 1;
                break;
            case 2:     // down onto the box
                on.Add(p + " Z");
                next = inStep > 0.1 && !movingZ && item ? 3 : 2;
                break;
            case 3:     // suction on
                on.UnionWith(new[] { p + " Z", p + " grab" });
                next = inStep >= 0.2 ? 4 : 3;
                break;
            case 4:     // up with the box
                on.Add(p + " grab");
                next = inStep > 0.1 && !movingZ ? 5 : 4;
                break;
            case 5:     // over to the other belt
                on.Add(p + " grab");
                on.UnionWith(over);
                next = inStep > 0.1 && !movingXy ? 6 : 5;
                break;
            case 6:     // down onto the belt
                on.UnionWith(new[] { p + " grab", p + " Z" });
                on.UnionWith(over);
                next = inStep > 0.1 && !movingZ ? 7 : 6;
                break;
            case 7:     // suction off
                on.Add(p + " Z");
                on.UnionWith(over);
                next = inStep >= 0.2 ? 8 : 7;
                break;
            case 8:     // up
                on.UnionWith(over);
                next = inStep > 0.1 && !movingZ ? 9 : 8;
                break;
            case 9:     // back
                next = inStep > 0.1 && !movingXy ? 0 : 9;
                if (next == 0)
                {
                    _stationCycles++;
                }

                break;
        }

        if (next != _stationStep)
        {
            _stationStep = next;
            _stationSince = _elapsed;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// Two belts side by side under a pick and place station, built with the editor: boxes arrive
    /// on the first and must leave on the second, carried over by the station under the PLC
    /// program above. First the two-axis station, then the three-axis one (which also uses Y).
    /// A box that is not picked up runs off the end of the first belt and counts as fallen.
    /// </summary>
    private void RunStation(double delta)
    {
        _wallClock += delta;
        Remover? remover = Main.View.Find<Remover>("Remover");
        PickAndPlace? station = Main.View.Find<PickAndPlace>("Pick and place");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
                BuildStationLine("two_axis_pick_and_place", beltX: 0.5f, placeZ: 0.0f);
                SortersCheck("two-axis tags", !Main.Tags.ActuatorNames.Contains("Pick and place Y")
                    && new[] { "Pick and place X", "Pick and place Z", "Pick and place grab" }.All(Main.Tags.ActuatorNames.Contains));
                _controlsPhase = 1;
                break;

            case 1 when _wallClock >= 45.0:
                GD.Print($"SELFTEST station, two axes: cycles={_stationCycles} removed={remover?.Count} fallen={fallen} "
                    + $"emitted={Main.View.Emitters.Sum(e => e.EmittedCount)}");
                SortersCheck("two axes", remover is { Count: >= 4 } && fallen == 0 && _stationCycles >= 4);
                SaveScreenshot(suffix: "_2axis");

                // A reset while the station is at work must leave nothing behind.
                Main.Sim.Reset();
                _controlsPhase = 2;
                break;

            case 2 when _wallClock >= 46.0:
                SortersCheck("reset", station is { Holding: false, ItemDetected: false } && Main.CountBoxes().InScene == 0
                    && station.TipPosition.DistanceTo(station.GlobalTransform * new Vector3(station.RestX, station.TipHeight, 0.0f)) < 0.001f);
                BuildStationLine("pick_and_place", beltX: 0.75f, placeZ: 0.5f);
                SortersCheck("three-axis tags", Main.Tags.ActuatorNames.Contains("Pick and place Y")
                    && Main.Tags.SensorNames.Contains("Pick and place moving Y"));
                _controlsPhase = 3;
                break;

            case 3 when _wallClock >= 91.0:
                GD.Print($"SELFTEST station, three axes: cycles={_stationCycles} removed={remover?.Count} fallen={fallen} "
                    + $"emitted={Main.View.Emitters.Sum(e => e.EmittedCount)}");
                SortersCheck("three axes", remover is { Count: >= 4 } && fallen == 0 && _stationCycles >= 4);
                SaveScreenshot(suffix: "_3axis");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// Station at the origin; infeed belt under the gripper's rest position, outfeed belt under its
    /// far position, both running towards +Z; a sensor looking across the infeed belt at z = 0.
    /// </summary>
    private void BuildStationLine(string stationId, float beltX, float placeZ)
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();
        bool built = Editor.Place(stationId, 0.0f, 0.0f) is not null
            && Editor.Place("belt_conveyor_2m", -beltX, -0.75f, 270.0f) is not null
            && Editor.Place("emitter_gantry", -beltX, -1.25f, 270.0f) is not null
            && Editor.Place("belt_conveyor_2m", beltX, placeZ + 0.75f, 270.0f) is not null
            && Editor.Place("remover_hood_1m0", beltX, placeZ + 2.0f, 270.0f) is not null
            && Editor.Place("diffuse_sensor", -beltX - 0.75f, 0.0f, 90.0f) is not null;
        Editor.SetDesignMode(false);
        SortersCheck($"{stationId} line built", built);
        _stationHasY = stationId == "pick_and_place";
        _stationStep = 0;
        _stationSince = 0.0;
        _stationCycles = 0;
        _elapsed = 0.0;
        Main.Camera.Frame(new Vector3(0.0f, 1.0f, 0.3f), 6.5f);
        Main.Sim.Run();
    }

    /// <summary>
    /// The PLC program for the "heavy" test, on tags only. Straight on: everything runs. Diverting:
    /// feed a box until the sensor at the centre of the turntable or chain transfer sees it, stop
    /// the infeed, turn the table (or raise the chains), carry the box off to the side lane, and
    /// go back for the next one.
    /// </summary>
    private void RunHeavyPlc()
    {
        if (!Main.Tags.SensorNames.Contains("Sensor"))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        bool atCentre = Sensor(sensors, "Sensor");
        string device = _heavyTable ? "Turntable" : "Chain transfer";
        var on = new System.Collections.Generic.HashSet<string> { "Conveyor 2", "Conveyor 3" };

        if (!_heavyDivert)
        {
            on.UnionWith(new[] { "Conveyor", "Emitter", device });
        }
        else
        {
            switch (_heavyStep)
            {
                case 0:     // feed until a box is at the centre
                    on.UnionWith(new[] { "Conveyor", "Emitter", device });
                    if (atCentre)
                    {
                        _heavyStep = 1;
                    }

                    break;

                case 1:     // turn the table with the box on it; the chain transfer needs no positioning
                    if (_heavyTable)
                    {
                        on.Add("Turntable turn");
                    }

                    if (!_heavyTable || Sensor(sensors, "Turntable at 90"))
                    {
                        _heavyStep = 2;
                        _heavyClearAt = _elapsed;
                    }

                    break;

                case 2:     // carry the box off; allow 2.5 s after it left the sensor
                    on.UnionWith(_heavyTable ? new[] { "Turntable turn", "Turntable" } : new[] { "Chain transfer left" });
                    if (atCentre)
                    {
                        _heavyClearAt = _elapsed;
                    }
                    else if (_elapsed - _heavyClearAt > 2.5)
                    {
                        _heavyStep = 3;
                    }

                    break;

                case 3:     // back to the home position
                    if (!_heavyTable || Sensor(sensors, "Turntable at 0"))
                    {
                        _heavyStep = 0;
                    }

                    break;
            }
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// Two roller lines built with the editor, each with a straight exit (remover "Remover") and a
    /// side lane to the left (remover "Remover 2"): first with a turntable, then with a chain
    /// transfer. Each is run straight on and then diverting, under the PLC program above.
    /// </summary>
    private void RunHeavy(double delta)
    {
        _wallClock += delta;
        Remover? straight = Main.View.Find<Remover>("Remover");
        Remover? side = Main.View.Find<Remover>("Remover 2");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
                BuildHeavyLine("turntable", deviceX: 2.0f, exitX: 4.0f, sideZ: -2.0f);
                _heavyTable = true;
                SortersCheck("turntable built", new[] { "Turntable", "Turntable reverse", "Turntable turn" }.All(Main.Tags.ActuatorNames.Contains)
                    && new[] { "Turntable at 0", "Turntable at 90", "Sensor" }.All(Main.Tags.SensorNames.Contains));
                _controlsPhase = 1;
                break;

            case 1 when _wallClock >= 20.0:
                GD.Print($"SELFTEST heavy, turntable straight: straight={straight?.Count} side={side?.Count} fallen={fallen}");
                SortersCheck("turntable straight", straight is { Count: >= 2 } && side is { Count: 0 } && fallen == 0
                    && PanelSensor("Turntable at 0") && !PanelSensor("Turntable at 90"));
                _heavyStep = 0;
                _heavyDivert = true;
                _controlsPhase = 2;
                break;

            case 2 when _wallClock >= 28.0:
                _countA = straight?.Count ?? 0;
                _countB = side?.Count ?? 0;
                _controlsPhase = 3;
                break;

            case 3 when _wallClock >= 68.0:
                GD.Print($"SELFTEST heavy, turntable turning: straight=+{straight?.Count - _countA} side=+{side?.Count - _countB} fallen={fallen}");
                SortersCheck("turntable turning", straight is not null && straight.Count == _countA
                    && side is not null && side.Count - _countB >= 2 && fallen == 0);
                SaveScreenshot(suffix: "_turntable");

                BuildHeavyLine("chain_transfer", deviceX: 1.75f, exitX: 3.5f, sideZ: -1.75f);
                _heavyTable = false;
                _heavyDivert = false;
                _heavyStep = 0;
                SortersCheck("chain transfer built",
                    new[] { "Chain transfer", "Chain transfer left", "Chain transfer right" }.All(Main.Tags.ActuatorNames.Contains));
                _controlsPhase = 4;
                break;

            case 4 when _wallClock >= 88.0:
                GD.Print($"SELFTEST heavy, chains down: straight={straight?.Count} side={side?.Count} fallen={fallen}");
                SortersCheck("chains down", straight is { Count: >= 2 } && side is { Count: 0 } && fallen == 0);
                _heavyDivert = true;
                _controlsPhase = 5;
                break;

            case 5 when _wallClock >= 96.0:
                _countA = straight?.Count ?? 0;
                _countB = side?.Count ?? 0;
                _controlsPhase = 6;
                break;

            case 6 when _wallClock >= 126.0:
                GD.Print($"SELFTEST heavy, chains left: straight=+{straight?.Count - _countA} side=+{side?.Count - _countB} fallen={fallen}");
                SortersCheck("chains left", straight is not null && straight.Count == _countA
                    && side is not null && side.Count - _countB >= 2 && fallen == 0);
                SaveScreenshot(suffix: "_chain");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// Roller conveyor along +X, the device under test, a straight exit and a side lane towards -Z,
    /// with a sensor looking across the centre of the device at box height.
    /// </summary>
    private void BuildHeavyLine(string device, float deviceX, float exitX, float sideZ)
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();
        bool built = Editor.Place("roller_conveyor_2m", 0.0f, 0.0f) is not null
            && Editor.Place("emitter_gantry", -0.5f, 0.0f) is not null
            && Editor.Place(device, deviceX, 0.0f) is not null
            && Editor.Place("roller_conveyor_2m", exitX, 0.0f) is not null
            && Editor.Place("remover_hood_1m0", exitX + 1.25f, 0.0f) is not null
            && Editor.Place("roller_conveyor_2m", deviceX, sideZ, 90.0f) is not null
            && Editor.Place("remover_hood_1m0", deviceX, sideZ - 1.25f, 90.0f) is not null
            && Editor.Place("diffuse_sensor", deviceX, 1.0f, 180.0f) is not null;
        Editor.SetSelectedHeight(0.6f);      // the roller line is 0.5 m high; the default 0.9 m looks over the boxes
        Editor.SetDesignMode(false);
        SortersCheck($"{device} line built", built);
        Main.Camera.Frame(new Vector3(deviceX, 0.5f, -0.8f), 7.0f);
        Main.Sim.Run();
    }

    /// <summary>
    /// The transport parts of plan 02. Each scene holds several lanes side by side, 3 m apart, each
    /// with its own emitter and remover, so one run tests them all. The PLC switches everything on.
    /// Removers are named in the order the lanes are placed: "Remover", "Remover 2", ...
    /// </summary>
    private void RunTransport(double delta)
    {
        _wallClock += delta;
        Remover? Lane(int number) => Main.View.Find<Remover>(number == 1 ? "Remover" : $"Remover {number}");

        switch (_controlsPhase)
        {
            case 0:
            {
                // Lane 1: belt, spur, scale and gate in a row. Lane 2: a belt up the inclined belt to 1.8 m,
                // with high boxes only (they tip first). Lane 3: the same, run the other way: turned round,
                // downhill. Lane 4: pallets up the incline.
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = PlaceAt("belt_conveyor_2m", 0.0f, 0.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 0.0f)
                    && PlaceAt("spur_conveyor", 1.75f, 0.0f)
                    && PlaceAt("conveyor_scale", 3.0f, 0.0f)
                    && PlaceAt("belt_conveyor_gate", 4.5f, 0.0f)
                    && PlaceAt("remover_hood_1m0", 5.75f, 0.0f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 3.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 3.0f)
                    && SetParam("highRatio", 1.0)
                    && PlaceAt("belt_conveyor_inclined", 3.0f, 3.0f)
                    && PlaceAt("belt_conveyor_2m", 6.0f, 3.0f, height: 1.8f)
                    && PlaceAt("remover_hood_1m0", 7.25f, 3.0f, height: 1.8f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 6.0f, height: 1.8f)
                    && PlaceAt("emitter_gantry", -0.5f, 6.0f, height: 1.8f)
                    && SetParam("highRatio", 1.0)
                    && PlaceAt("belt_conveyor_inclined", 3.0f, 6.0f, yaw: 180.0f)
                    && SetParam(SceneTags.Reverse, 1.0)
                    && PlaceAt("belt_conveyor_2m", 6.0f, 6.0f)
                    && PlaceAt("remover_hood_1m0", 7.25f, 6.0f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 9.0f)
                    && PlaceAt("emitter_pallet", 0.0f, 9.0f, height: 0.8f)
                    && SetParam("interval", 5.0)
                    && PlaceAt("belt_conveyor_inclined", 3.0f, 9.0f)
                    && PlaceAt("belt_conveyor_2m", 6.0f, 9.0f, height: 1.8f)
                    && PlaceAt("remover_hood_1m0", 7.25f, 9.0f, height: 1.8f);
                Editor.SetDesignMode(false);
                SortersCheck("conveyor lanes built", built && Enumerable.Range(1, 4).All(number => Lane(number) is not null)
                    && new[] { "Conveyor", "Conveyor 13" }.All(Main.Tags.ActuatorNames.Contains));
                Main.Camera.Frame(new Vector3(3.5f, 1.0f, 4.5f), 14.0f);
                Main.Sim.Run();
                _transportSince = _wallClock;
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock - _transportSince >= 45.0:
            {
                (int _, int fallen) = Main.CountBoxes();
                GD.Print($"SELFTEST transport, conveyors: arrived={Lane(1)?.Count}/{Lane(2)?.Count}/{Lane(3)?.Count}/{Lane(4)?.Count} fallen={fallen} upright={BoxesUpright()}");
                SortersCheck("spur, scale and gate", Lane(1) is { Count: >= 4 });
                GD.Print($"SELFTEST transport: highest weight on the scale {_scaleMaxWeight / 100.0:0.00} kg");
                SortersCheck("the scale weighs the boxes", _scaleMaxWeight > 0);
                SortersCheck("incline up", Lane(2) is { Count: >= 4 });
                SortersCheck("incline down", Lane(3) is { Count: >= 4 });
                SortersCheck("pallets up the incline", Lane(4) is { Count: >= 3 });
                SortersCheck("nothing fell or tipped", fallen == 0 && BoxesUpright());
                SaveScreenshot(suffix: "_conveyors");

                // Passive parts, one lane each. Lane 1: a belt to a chute that drops into a remover below.
                // Lane 2: the same from a roller conveyor down the low chute. Lane 3: boxes put on the belt
                // 0.25 m off the centre line leave the aligner in the middle. Lane 4: a corner stop across
                // the belt holds the boxes back. Lane 5: pallets roll on from a roller conveyor over three
                // free rollers into a remover. Lane 6: boxes 0.25 m off the centre line are stopped under the
                // positioning bars, clamped into the middle and sent on (PLC in RunTransportPlc). One box at a time: a box
                // waiting just behind would stand in the blades' way and be pushed askew. Lane 7: pallets over a
                // roller bend. Lane 8: a roller stop holds the pallets back for 28 s and then lets them go.
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool passiveBuilt = PlaceAt("belt_conveyor_2m", 0.0f, 0.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 0.0f)
                    && PlaceAt("chute", 1.75f, 0.0f)
                    && PlaceAt("remover_hood_1m0", 2.75f, 0.0f, height: 0.3f)

                    && PlaceAt("roller_conveyor_2m", 0.0f, 3.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 3.0f)
                    && PlaceAt("chute_low", 1.75f, 3.0f)
                    && PlaceAt("remover_hood_1m0", 2.75f, 3.0f, height: 0.2f)

                    && PlaceAt("belt_conveyor_4m", 0.0f, 6.0f)
                    && PlaceAt("emitter_gantry", -1.5f, 6.25f)
                    && PlaceAt("aligner", 0.0f, 6.0f)
                    && PlaceAt("remover_hood_1m0", 2.25f, 6.0f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 9.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 9.0f)
                    && PlaceAt("metal_corner", 0.75f, 8.75f)
                    && PlaceAt("remover_hood_1m0", 1.25f, 9.0f)

                    && PlaceAt("roller_conveyor_2m", 0.0f, 12.0f)
                    && PlaceAt("emitter_pallet", 0.0f, 12.0f)
                    && SetParam("interval", 6.0)
                    && PlaceAt("free_roller", 1.5f, 12.0f)
                    && PlaceAt("free_roller", 2.5f, 12.0f)
                    && PlaceAt("free_roller", 3.5f, 12.0f)
                    && PlaceAt("remover_hood_1m4", 4.25f, 12.0f, height: 0.5f)

                    && PlaceAt("belt_conveyor_4m", 1.0f, 15.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 15.25f)
                    && SetParam("interval", 8.0)
                    && PlaceAt("positioning_bars", 2.0f, 15.0f)
                    && PlaceAt("diffuse_sensor", 2.25f, 15.75f, yaw: 180.0f)
                    && PlaceAt("remover_hood_1m0", 3.25f, 15.0f)

                    && PlaceAt("roller_conveyor_2m", 0.0f, -3.0f)
                    && PlaceAt("emitter_pallet", 0.0f, -3.0f)
                    && SetParam("interval", 6.0)
                    && PlaceAt("roller_conveyor_curved", 1.0f, -4.5f, yaw: 270.0f)
                    && PlaceAt("roller_conveyor_2m", 2.5f, -5.5f, yaw: 90.0f)
                    && PlaceAt("remover_hood_1m4", 2.5f, -6.75f, yaw: 90.0f, height: 0.5f)

                    && PlaceAt("roller_conveyor_4m", 0.0f, -10.0f)
                    && PlaceAt("emitter_pallet", -1.0f, -10.0f)
                    && SetParam("interval", 5.0)
                    && PlaceAt("roller_stop", 1.0f, -10.0f)
                    && PlaceAt("remover_hood_1m4", 2.25f, -10.0f, height: 0.5f);
                Editor.SetDesignMode(false);
                SortersCheck("passive lanes built", passiveBuilt && Enumerable.Range(1, 8).All(number => Lane(number) is not null));
                Main.Camera.Frame(new Vector3(1.5f, 0.6f, 3.0f), 28.0f);
                Main.Sim.Run();
                _transportSince = _wallClock;
                _controlsPhase = 2;
                break;
            }

            case 2:
            {
                SampleAligner();
                if (!_stopChecked && _wallClock - _transportSince >= 26.0)
                {
                    // Just before the stop goes down: pallets stand against it and none has come through.
                    _stopChecked = true;
                    _stopHeld = Lane(8) is { Count: 0 } && GetTree().GetNodesInGroup(Box.Group).OfType<Box>().Any(box =>
                        !box.IsQueuedForDeletion() && !box.IsStandardBox && Mathf.Abs(box.GlobalPosition.Z + 10.0f) < 0.5f
                        && box.GlobalPosition.X is > 0.2f and < 0.5f);
                }

                if (_wallClock - _transportSince < 50.0)
                {
                    break;
                }

                Box[] boxes = GetTree().GetNodesInGroup(Box.Group).OfType<Box>().Where(box => !box.IsQueuedForDeletion()).ToArray();
                Box[] atCorner = boxes.Where(box => Mathf.Abs(box.GlobalPosition.Z - 9.0f) < 0.6f).ToArray();
                double before = _alignerBefore > 0 ? _alignerBeforeSum / _alignerBefore : 0.0;
                GD.Print($"SELFTEST transport, passive: chute={Lane(1)?.Count} low chute={Lane(2)?.Count} aligner={Lane(3)?.Count} "
                    + $"corner={Lane(4)?.Count} (waiting {atCorner.Length}) free rollers={Lane(5)?.Count}");
                GD.Print($"SELFTEST transport, aligner: before {before * 100.0:F1} cm off centre ({_alignerBefore} samples), "
                    + $"after at most {_alignerAfterMax * 100.0:F1} cm ({_alignerAfter} samples)");
                SortersCheck("chute", Lane(1) is { Count: >= 3 });
                SortersCheck("low chute", Lane(2) is { Count: >= 3 });
                SortersCheck("aligner puts boxes in the middle", Lane(3) is { Count: >= 3 } && _alignerBefore >= 20 && before >= 0.15
                    && _alignerAfter >= 20 && _alignerAfterMax <= 0.03);
                SortersCheck("corner stop holds boxes back", Lane(4) is { Count: 0 } && atCorner.Length >= 2
                    && atCorner.All(box => box.GlobalPosition.X < 0.76f));
                SortersCheck("pallets roll over free rollers", Lane(5) is { Count: >= 2 });
                GD.Print($"SELFTEST transport, positioning bars: clamped {_clampCycles} boxes, worst {_clampWorst * 100.0:F1} cm off centre, "
                    + $"arrived={Lane(6)?.Count}");
                SortersCheck("positioning bars put boxes in the middle and let them go", Lane(6) is { Count: >= 2 }
                    && _clampCycles >= 2 && _clampWorst <= 0.01);
                GD.Print($"SELFTEST transport, roller bend and stop: bend={Lane(7)?.Count} stop held a pallet={_stopHeld} "
                    + $"through the stop after it went down={Lane(8)?.Count}");
                SortersCheck("pallets over the roller bend", Lane(7) is { Count: >= 3 });
                SortersCheck("roller stop holds a pallet and then lets it go", _stopHeld && Lane(8) is { Count: >= 1 });
                SortersCheck("nothing on the floor", boxes.All(box => box.GlobalPosition.Y > 0.12f));
                SaveScreenshot(suffix: "_passive");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>
    /// The PLC of the transport test: everything on, except in the lane of the positioning bars. There
    /// it feeds until the photo-eye sees a box, stops the belt, closes the clamps, waits for "closed",
    /// notes how far the box lies from the centre line, opens the clamps and carries on.
    /// </summary>
    private void RunTransportPlc()
    {
        string? weightTag = Main.Tags.AnalogSensorNames.FirstOrDefault(name => name.EndsWith(" weight", StringComparison.Ordinal));
        if (weightTag is not null)
        {
            _scaleMaxWeight = Math.Max(_scaleMaxWeight, PanelNumber(weightTag));
        }

        var on = new System.Collections.Generic.HashSet<string>(Main.Tags.ActuatorNames);
        if (Main.Tags.SensorNames.Contains("Clamp closed"))
        {
            bool[] sensors = Main.Tags.SnapshotSensors();
            on.Remove("Clamp");
            switch (_clampStep)
            {
                case 0:     // feed until the photo-eye sees a box
                    if (Sensor(sensors, "Sensor"))
                    {
                        _clampStep = 1;
                        _clampAt = _elapsed;
                    }

                    break;

                case 1:     // belt off, clamps closing; when they stop, see where the box lies
                    on.Remove("Conveyor 6");
                    on.Add("Clamp");
                    if (Sensor(sensors, "Clamp closed") && _elapsed - _clampAt > 0.3)
                    {
                        double[] offsets = GetTree().GetNodesInGroup(Box.Group).OfType<Box>()
                            .Where(box => !box.IsQueuedForDeletion() && Mathf.Abs(box.GlobalPosition.X - 2.0f) < 0.35f
                                && Mathf.Abs(box.GlobalPosition.Z - 15.0f) < 0.6f)
                            .Select(box => (double)Mathf.Abs(box.GlobalPosition.Z - 15.0f)).ToArray();
                        if (offsets.Length > 0)
                        {
                            _clampCycles++;
                            _clampWorst = Math.Max(_clampWorst, offsets.Max());
                        }

                        _clampStep = 2;
                    }

                    break;

                case 2:     // clamps off, belt still off until they are open
                    on.Remove("Conveyor 6");
                    if (Sensor(sensors, "Clamp open"))
                    {
                        _clampStep = 3;
                        _clampAt = _elapsed;
                    }

                    break;

                case 3:     // carry on until the box has been gone from the photo-eye for a second
                    if (Sensor(sensors, "Sensor"))
                    {
                        _clampAt = _elapsed;
                    }
                    else if (_elapsed - _clampAt > 1.0)
                    {
                        _clampStep = 0;
                    }

                    break;
            }
        }

        // The roller stop holds the pallets back for the first 28 s of the scene and then lets them go.
        if (_controlsPhase == 2 && _wallClock - _transportSince >= 28.0)
        {
            on.Remove("Stop");
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>Lane 3 of the passive parts: how far boxes lie off the centre line before and after the aligner.</summary>
    private void SampleAligner()
    {
        foreach (Box box in GetTree().GetNodesInGroup(Box.Group).OfType<Box>())
        {
            float off = Mathf.Abs(box.GlobalPosition.Z - 6.0f);
            float x = box.GlobalPosition.X;
            if (box.IsQueuedForDeletion() || off > 0.8f)
            {
                continue;
            }

            if (x is > -1.3f and < -0.9f)
            {
                _alignerBefore++;
                _alignerBeforeSum += off;
            }
            else if (x is > 0.9f and < 1.7f)
            {
                _alignerAfter++;
                _alignerAfterMax = Math.Max(_alignerAfterMax, off);
            }
        }
    }

    private int SensEdges(string name) => _sensEdges.TryGetValue(name, out int count) ? count : 0;

    /// <summary>
    /// The PLC of the sensors test: everything on, and the edges of the sensor tags counted the way a PLC
    /// counts them, plus a note of every item that passed the sensors, to compare the two.
    /// </summary>
    private void RunSensorsPlc()
    {
        // Everything runs; the emitters stop a little before the end so that the belts run empty and every item is counted.
        bool emitting = _controlsPhase != 1 || _wallClock - _transportSince < 28.0;
        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(name => emitting || !name.StartsWith("Emitter", StringComparison.Ordinal)).ToArray());
        if (!Main.Tags.SensorNames.Contains("Encoder A"))
        {
            return;
        }

        bool[] sensors = Main.Tags.SnapshotSensors();
        if (_sensStart < 0.0)
        {
            _sensStart = _elapsed;
        }

        // Int tags: the kind of item the vision sensor sees, the number of interrupted beams.
        int seen = PanelNumber("Vision sensor");
        if (seen > 0)
        {
            _visionNumbers.Add(seen);
        }

        _visionDetected |= Sensor(sensors, "Vision sensor detected");
        _lightArrayMaxCount = Math.Max(_lightArrayMaxCount, PanelNumber("Light array count"));

        foreach (string name in new[] { "Capacitive sensor", "Inductive sensor", "Retroreflective sensor", "Retroreflective sensor 2", "Light array", "Encoder A" })
        {
            bool now = Sensor(sensors, name);
            if (now && !(_sensPrevious.TryGetValue(name, out bool was) && was))
            {
                _sensEdges[name] = SensEdges(name) + 1;
                // Moving forward B follows A: at a rising edge of A, B is still low.
                if (name == "Encoder A" && Sensor(sensors, "Encoder B"))
                {
                    _sensForward = false;
                }
            }

            _sensPrevious[name] = now;
        }

        foreach (Box box in GetTree().GetNodesInGroup(Box.Group).OfType<Box>())
        {
            if (box.IsQueuedForDeletion())
            {
                continue;
            }

            Vector3 at = box.GlobalPosition;
            if (Mathf.Abs(at.Z) < 0.5f && at.X > 1.7f)
            {
                (FactorySim.Core.ItemKinds.IsMetal(box.ItemModel) ? _sensMetal : _sensOther).Add(box.GetInstanceId());
            }
            else if (Mathf.Abs(at.Z - 3.0f) < 0.5f && at.X > 0.6f)
            {
                _sensLane2.Add(box.GetInstanceId());
            }
            else if (Mathf.Abs(at.Z - 6.0f) < 0.5f && at.X > 0.6f)
            {
                _sensLane3.Add(box.GetInstanceId());
            }
        }
    }

    /// <summary>
    /// The sensors of plan 03 in one scene, one lane each (3 m apart). Lane 1: blue and metal raw material
    /// past a capacitive and an inductive sensor. Lane 2: a reflection sensor with its reflector, and a
    /// second one without. Lane 3: a light array across a belt. Lane 4: an encoder on a running belt.
    /// Lane 5: the safety door, clicked open and shut like the operator does.
    /// </summary>
    private void RunSensors(double delta)
    {
        _wallClock += delta;
        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = PlaceAt("belt_conveyor_4m", 0.0f, 0.0f)
                    && PlaceAt("emitter_raw_blue", -1.5f, 0.0f)
                    && PlaceAt("emitter_raw_metal", -0.5f, 0.0f)
                    && PlaceAt("capacitive_sensor", 1.0f, 0.25f, yaw: 180.0f, height: 0.88f)
                    && SetParam("range", 0.3)
                    && PlaceAt("inductive_sensor", 1.5f, 0.25f, yaw: 180.0f, height: 0.88f)
                    && SetParam("range", 0.3)
                    && PlaceAt("vision_sensor", 0.5f, 0.25f, yaw: 180.0f, height: 0.88f)
                    && SetParam("range", 0.3)
                    && PlaceAt("remover_hood_1m0", 2.25f, 0.0f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 3.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 3.0f)
                    && PlaceAt("retroreflective_sensor", 0.0f, 3.75f, yaw: 180.0f)
                    && PlaceAt("reflector", 0.0f, 2.25f)
                    && PlaceAt("retroreflective_sensor", -1.0f, 5.5f, yaw: 180.0f)
                    && PlaceAt("remover_hood_1m0", 1.25f, 3.0f)

                    && PlaceAt("belt_conveyor_2m", 0.0f, 6.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 6.0f)
                    && PlaceAt("light_array_receiver", 0.5f, 6.75f, yaw: 180.0f)
                    && PlaceAt("light_array_emitter", 0.5f, 5.25f)
                    && PlaceAt("remover_hood_1m0", 1.25f, 6.0f)

                    && PlaceAt("belt_conveyor_4m", 0.0f, 9.0f)
                    && PlaceAt("incremental_encoder", 0.0f, 9.0f)

                    && PlaceAt("safety_door", 0.0f, 12.0f);
                string warning = Editor.SensorWarning;
                Editor.SetDesignMode(false);
                SortersCheck("sensors built", built && new[] { "Capacitive sensor", "Inductive sensor", "Retroreflective sensor", "Light array" }
                    .All(Main.Tags.SensorNames.Contains) && new[] { "Encoder A", "Encoder B", "Safety door" }.All(Main.Tags.SensorNames.Contains));
                SortersCheck("a sensor without partner is warned about", warning.Contains("Retroreflective sensor 2", StringComparison.Ordinal));
                Main.Camera.Frame(new Vector3(1.0f, 0.8f, 6.0f), 20.0f);
                Main.Sim.Run();
                _transportSince = _wallClock;
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock - _transportSince >= 38.0:
            {
                int Edges(string name) => SensEdges(name);
                double expected = 0.6 * (_elapsed - _sensStart) / 0.05;
                GD.Print($"SELFTEST sensors, raw material: passed {_sensMetal.Count} metal and {_sensOther.Count} blue; capacitive saw {Edges("Capacitive sensor")}, inductive {Edges("Inductive sensor")}");
                GD.Print($"SELFTEST sensors, reflection: passed {_sensLane2.Count}, sensor saw {Edges("Retroreflective sensor")}, sensor without reflector {Edges("Retroreflective sensor 2")}");
                GD.Print($"SELFTEST sensors, light array: passed {_sensLane3.Count}, array saw {Edges("Light array")}");
                GD.Print($"SELFTEST sensors, encoder: {Edges("Encoder A")} pulses of A, expected about {expected:F0}, forward={_sensForward}");
                SortersCheck("capacitive sensor sees every item", _sensMetal.Count >= 3 && _sensOther.Count >= 3
                    && Edges("Capacitive sensor") == _sensMetal.Count + _sensOther.Count);
                SortersCheck("inductive sensor sees only the metal ones", Edges("Inductive sensor") == _sensMetal.Count);
                SortersCheck("reflection sensor sees every box", _sensLane2.Count >= 3 && Edges("Retroreflective sensor") == _sensLane2.Count);
                SortersCheck("reflection sensor without reflector sees nothing", Edges("Retroreflective sensor 2") == 0);
                SortersCheck("light array sees every box", _sensLane3.Count >= 3 && Edges("Light array") == _sensLane3.Count);
                SortersCheck("encoder counts the distance, forward", Math.Abs(Edges("Encoder A") - expected) <= 2.0 && _sensForward);
                GD.Print($"SELFTEST sensors, vision: numbers seen {string.Join(", ", _visionNumbers.OrderBy(n => n))}, detected={_visionDetected}; light array at most {_lightArrayMaxCount} beams");
                SortersCheck("vision sensor tells blue and metal raw material apart",
                    _visionDetected && _visionNumbers.SetEquals(new[] { FactorySim.Core.ItemKinds.Number("raw_material_blue"), FactorySim.Core.ItemKinds.Number("raw_material_metal") }));
                SortersCheck("light array counts interrupted beams", _lightArrayMaxCount is >= 1 and <= 8);

                // The safety door: shut at rest, a click opens it, another click shuts it.
                OperatorControl? door = Main.View.Find<OperatorControl>("Safety door");
                SortersCheck("door shut at rest", door is { Swing: 0.0f } && PanelSensor("Safety door"));
                Main.Camera.Frame(new Vector3(0.0f, 1.0f, 12.0f), 5.0f);
                _transportSince = _wallClock;
                _controlsPhase = 2;
                break;
            }

            case 2 when _wallClock - _transportSince >= 0.5:
            {
                OperatorControl? door = Main.View.Find<OperatorControl>("Safety door");
                Vector2 at = door is null ? Vector2.Zero : Main.Camera.UnprojectPosition(door.PickPoint);
                SortersCheck("a click on the door is picked up", door is not null && Main.ControlAt(at) == door);
                Main.Click(at, pressed: true);
                Main.Click(at, pressed: false);
                _transportSince = _wallClock;
                _controlsPhase = 3;
                break;
            }

            case 3 when _wallClock - _transportSince >= 1.0:
            {
                OperatorControl? door = Main.View.Find<OperatorControl>("Safety door");
                SortersCheck("door open after a click", door is { Swing: >= 89.0f } && !PanelSensor("Safety door"));
                Vector2 at = door is null ? Vector2.Zero : Main.Camera.UnprojectPosition(door.PickPoint);
                Main.Click(at, pressed: true);
                Main.Click(at, pressed: false);
                _transportSince = _wallClock;
                _controlsPhase = 4;
                break;
            }

            case 4 when _wallClock - _transportSince >= 1.0:
            {
                OperatorControl? door = Main.View.Find<OperatorControl>("Safety door");
                SortersCheck("door shut after the second click", door is { Swing: <= 1.0f } && PanelSensor("Safety door"));
                SaveScreenshot(suffix: "_sensors");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
            }
        }
    }

    private bool SetEmit(string itemId)
    {
        Editor.SetSelectedEmits(itemId);
        return true;
    }

    /// <summary>
    /// The PLC of the assembly test, on tags only. One cycle: feed a lid along the lid belt and a base along the
    /// base belt up to the stop; close the positioning bars on the base; take the lid with the three-axis pick
    /// and place; set it on the base; let go (the lid and the base become a final product); open the bars;
    /// lower the stop and carry the product away. Emitters are pulsed, so that one item is fed at a time.
    /// </summary>
    private void RunAssemblyPlc()
    {
        if (!Main.Tags.SensorNames.Contains("Clamp closed"))
        {
            Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(_ => true).ToArray());     // the little emitter test
            return;
        }

        const string p = "Pick and place";
        bool[] sensors = Main.Tags.SnapshotSensors();
        double t = _elapsed;
        double inStep = t - _asmStepAt;
        bool movingZ = Sensor(sensors, p + " moving Z");
        bool movingXy = Sensor(sensors, p + " moving X") || Sensor(sensors, p + " moving Y");
        var on = new System.Collections.Generic.HashSet<string>();

        // What passed the stop that should not have: a loose base or lid in the lane of the base belt, far beyond the stop.
        foreach (Box box in GetTree().GetNodesInGroup(Box.Group).OfType<Box>())
        {
            if (box.IsQueuedForDeletion() || box.ItemModel is not { } model)
            {
                continue;
            }

            if (model.StartsWith("final_product_", StringComparison.Ordinal))
            {
                _asmFinals.Add(box.GetInstanceId());
            }
            else if ((model.StartsWith("product_base_", StringComparison.Ordinal) || model.StartsWith("product_lid_", StringComparison.Ordinal))
                && Mathf.Abs(box.GlobalPosition.X - 0.75f) < 0.4f && box.GlobalPosition.Z > 1.5f && _asmLoosened.Add(box.GetInstanceId()))
            {
                _asmLoose++;
            }
        }

        // The lid lane: run until the lid is under the gripper (the photo-eye, then a little further).
        if (!_asmLidReady)
        {
            on.Add("Conveyor");
            if (Sensor(sensors, "Sensor") && _asmLidSeenAt < 0.0)
            {
                _asmLidSeenAt = t;
            }

            _asmLidReady = _asmLidSeenAt >= 0.0 && t - _asmLidSeenAt >= 0.12;
        }

        if (t < _asmLidPulseUntil)
        {
            on.Add("Emitter");
        }

        // The base lane: the stop is up until the product is on its way; run until the base lies against it.
        if (_asmStep < 10)
        {
            on.Add("Stop");
        }

        if (!_asmBaseReady && _asmStep < 10)
        {
            on.Add("Conveyor 2");
            if (Sensor(sensors, "Sensor 2") && _asmBaseSeenAt < 0.0)
            {
                _asmBaseSeenAt = t;
            }

            _asmBaseReady = _asmBaseSeenAt >= 0.0 && t - _asmBaseSeenAt >= 0.6;
        }

        if (t < _asmBasePulseUntil)
        {
            on.Add("Emitter 2");
        }

        int next = _asmStep;
        switch (_asmStep)
        {
            case 0:     // both are in place
                next = _asmLidReady && _asmBaseReady ? 1 : 0;
                break;
            case 1:     // clamp the base
                on.Add("Clamp");
                next = Sensor(sensors, "Clamp closed") && inStep >= 0.4 ? 2 : 1;
                break;
            case 2:     // down onto the lid
                on.UnionWith(new[] { "Clamp", p + " Z" });
                next = inStep > 0.1 && !movingZ && Sensor(sensors, p + " item") ? 3 : 2;
                break;
            case 3:     // suction on
                on.UnionWith(new[] { "Clamp", p + " Z", p + " grab" });
                next = inStep >= 0.2 ? 4 : 3;
                break;
            case 4:     // up with the lid; the next lid can come
                on.UnionWith(new[] { "Clamp", p + " grab" });
                if (inStep > 0.1 && !movingZ)
                {
                    next = 5;
                    _asmLidReady = false;
                    _asmLidSeenAt = -1.0;
                    _asmLidPulseUntil = t + 0.3;
                }

                break;
            case 5:     // over to the base
                on.UnionWith(new[] { "Clamp", p + " grab", p + " X", p + " Y" });
                next = inStep > 0.1 && !movingXy ? 6 : 5;
                break;
            case 6:     // down onto the base
                on.UnionWith(new[] { "Clamp", p + " grab", p + " X", p + " Y", p + " Z" });
                next = inStep > 0.1 && !movingZ ? 7 : 6;
                break;
            case 7:     // let go: the lid and the base join
                on.UnionWith(new[] { "Clamp", p + " X", p + " Y", p + " Z" });
                next = inStep >= 0.4 ? 8 : 7;
                break;
            case 8:     // up
                on.UnionWith(new[] { "Clamp", p + " X", p + " Y" });
                next = inStep > 0.1 && !movingZ ? 9 : 8;
                break;
            case 9:     // back to the lids
                on.Add("Clamp");
                next = inStep > 0.1 && !movingXy ? 10 : 9;
                break;
            case 10:    // bars open, stop down, carry the product away; then the next base
                on.Add("Conveyor 2");
                if (Sensor(sensors, "Sensor 2"))
                {
                    _asmClearAt = t;
                }

                if (Sensor(sensors, "Clamp open") && _asmClearAt >= 0.0 && t - _asmClearAt >= 1.0)
                {
                    _asmCycles++;
                    _asmDoneAt = _asmCycles == 3 ? t : _asmDoneAt;
                    _asmBaseReady = false;
                    _asmBaseSeenAt = -1.0;
                    _asmClearAt = -1.0;
                    _asmBasePulseUntil = _asmCycles < 3 ? t + 0.3 : 0.0;
                    next = 0;
                }

                break;
        }

        if (next != _asmStep)
        {
            GD.Print($"SELFTEST assembly plc: step {_asmStep} -> {next} at {t:0.0} s");
            _asmStep = next;
            _asmStepAt = t;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>
    /// First a small line whose emitter is set to the stackable box: only stackable boxes may arrive. Then a
    /// small production line with the three-axis pick and place at the middle: lids arrive on one belt and
    /// bases on another, held against a stop and clamped by positioning bars; the station sets each lid on a
    /// base and the two become a final product, which leaves on the belt of the bases. Three products must
    /// arrive, none of them as a loose base or lid, and nothing may fall.
    /// </summary>
    private void RunAssembly(double delta)
    {
        _wallClock += delta;
        Remover? remover = Main.View.Find<Remover>("Remover");
        (int _, int fallen) = Main.CountBoxes();

        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = PlaceAt("belt_conveyor_2m", 0.0f, 0.0f)
                    && PlaceAt("emitter_gantry", -0.5f, 0.0f)
                    && SetEmit("stackable_box")
                    && PlaceAt("remover_hood_1m0", 1.25f, 0.0f);
                Editor.SetDesignMode(false);
                SortersCheck("emitter line built", built);
                Main.Camera.Frame(new Vector3(0.5f, 0.8f, 0.0f), 4.0f);
                Main.Sim.Run();
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock >= 10.0:
            {
                GD.Print($"SELFTEST assembly, emitter set to the stackable box: arrived={remover?.Count} (other={remover?.OtherCount}, low={remover?.LowCount}, high={remover?.HighCount})");
                SortersCheck("only stackable boxes arrive", remover is { OtherCount: >= 3, LowCount: 0, HighCount: 0 });

                // The line is the example scene that ships with the app: open it the way a user does.
                string? loadError = Editor.LoadFrom("res://scenes/assembly_line.fsscene.json");
                bool built = loadError is null;
                SortersCheck("assembly line opened", built && Main.Tags.SensorNames.Contains("Clamp closed")
                    && Main.Tags.ActuatorNames.Contains("Pick and place Y") && Main.Tags.ActuatorNames.Contains("Stop"));
                Main.Camera.Frame(new Vector3(0.0f, 1.0f, 0.8f), 7.0f);
                Main.Sim.Run();
                _asmStep = 0;
                _asmStepAt = _elapsed;
                _asmLidReady = false;
                _asmBaseReady = false;
                _asmLidSeenAt = -1.0;
                _asmBaseSeenAt = -1.0;
                _asmLidPulseUntil = _elapsed + 0.3;
                _asmBasePulseUntil = _elapsed + 0.3;
                _asmClearAt = -1.0;
                _asmCycles = 0;
                _asmDoneAt = -1.0;
                _transportSince = _wallClock;
                _controlsPhase = 2;
                break;
            }

            case 2 when (_asmCycles >= 3 && _elapsed - _asmDoneAt > 8.0) || _wallClock - _transportSince >= 150.0:
            {
                GD.Print($"SELFTEST assembly: cycles={_asmCycles} products={_asmFinals.Count} arrived={remover?.Count} (other={remover?.OtherCount}) "
                    + $"loose past the stop={_asmLoose} fallen={fallen} after {_wallClock - _transportSince:0.0} s");
                SortersCheck("three products made", _asmCycles >= 3 && _asmFinals.Count >= 3);
                SortersCheck("products delivered", remover is { OtherCount: >= 3, LowCount: 0, HighCount: 0 } && remover.OtherCount == _asmFinals.Count);
                SortersCheck("no loose base or lid past the stop", _asmLoose == 0);
                SortersCheck("nothing fell", fallen == 0);
                SaveScreenshot(suffix: "_assembly");
                _reported = true;
                GD.Print(_sortersOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_sortersOk ? 0 : 1);
                break;
            }
        }
    }

    /// <summary>Places an asset with the editor, optionally at another height than its default; true when it was placed.</summary>
    private bool PlaceAt(string assetId, float x, float z, float yaw = 0.0f, float? height = null)
    {
        bool placed = Editor.Place(assetId, x, z, yaw) is not null;
        if (placed && height is { } y)
        {
            Editor.SetSelectedHeight(y);
        }

        return placed;
    }

    /// <summary>Sets a parameter on the item that was placed last; always true, so it chains with the placements.</summary>
    private bool SetParam(string name, double value)
    {
        Editor.SetSelectedParam(name, value);
        return true;
    }

    /// <summary>True when no item lies tipped over: its up axis still points more up than sideways.</summary>
    private bool BoxesUpright() => GetTree().GetNodesInGroup(Box.Group).OfType<Box>()
        .Where(box => !box.IsQueuedForDeletion()).All(box => box.GlobalBasis.Y.Y > 0.9f);

    private void SortersCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST {_mode} {what}: {(ok ? "ok" : "FAILED")}");
        _sortersOk &= ok;
    }

    private static bool PortAnswers(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("127.0.0.1", port);
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    /// <summary>Saves a frame next to the --screenshot path, with a suffix before the extension.</summary>
    private void SaveScreenshot(string suffix)
    {
        if (_screenshotPath is null)
        {
            return;
        }

        string path = System.IO.Path.ChangeExtension(_screenshotPath, null) + suffix + System.IO.Path.GetExtension(_screenshotPath);
        Error error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SELFTEST screenshot {path}: {error}");
        _screenshotTaken = true;
    }

    private void PanelCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST panel {what}: {(ok ? "ok" : "FAILED")}");
        _panelOk &= ok;
    }

    private bool PanelSensor(string name) => Sensor(Main.Tags.SnapshotSensors(), name);

    /// <summary>The number on the line of an Int sensor tag, as a PLC reads it.</summary>
    private int PanelNumber(string name)
    {
        int index = -1;
        for (int i = 0; i < Main.Tags.AnalogSensorNames.Count; i++)
        {
            if (Main.Tags.AnalogSensorNames[i] == name)
            {
                index = i;
            }
        }

        return index < 0 ? int.MinValue : Main.Tags.SnapshotSensorNumbers()[index];
    }

    private const double TankLow = 0.8;
    private const double TankHigh = 1.2;

    /// <summary>
    /// Plays the PLC for the tank: a two-point control that only reads "Tank scene" (cm) and writes
    /// the valves. The discharge valve is fully open; the fill valve opens below the low limit and
    /// closes above the high limit.
    /// </summary>
    private void RunTankPlc()
    {
        if (!_tankPlcOn)
        {
            Main.Tags.ApplyActuatorNumbers(new int[Main.Tags.AnalogActuatorNames.Count]);
            return;
        }

        double scene = PanelNumber("Tank level") / 100.0;
        double flow = PanelNumber("Tank flow") / 10.0;
        bool filling = _tankFilling;
        if (scene < TankLow)
        {
            filling = true;
        }
        else if (scene > TankHigh)
        {
            filling = false;
            _tankReachedHigh = true;
        }

        if (filling != _tankFilling)
        {
            _tankSwitches++;
            _tankFilling = filling;
        }

        if (_tankReachedHigh)
        {
            _tankLowest = Math.Min(_tankLowest, scene);
            _tankHighest = Math.Max(_tankHighest, scene);
        }

        _tankMaxFlow = Math.Max(_tankMaxFlow, flow);
        Main.Tags.ApplyActuatorNumbers(Main.Tags.AnalogActuatorNames
            .Select(name => name == "Tank fill valve" ? (filling ? 100 : 0) : name == "Tank discharge valve" ? 100 : 0).ToArray());
    }

    /// <summary>
    /// A tank built with the editor, with faster valves so the test stays short. The PLC keeps the
    /// scene between <see cref="TankLow"/> and <see cref="TankHigh"/>; checks the tags, that the scene
    /// stays near the limits once it got there, that the fill valve switches a few times, the flow
    /// tag, that the level tag follows the tank, and that a reset empties the tank.
    /// </summary>
    private void RunTank(double delta)
    {
        _wallClock += delta;
        Tank? tank = Main.View.Find<Tank>("Tank");
        switch (_controlsPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("tank", 0.0f, 0.0f) is not null
                    && SetParam("maxInflow", 1.0) && SetParam("maxOutflow", 1.0);
                Editor.SetDesignMode(false);
                bool tags = new[] { "Tank level", "Tank flow" }.All(name => Main.Tags.AnalogSensorNames.Contains(name))
                    && new[] { "Tank fill valve", "Tank discharge valve" }.All(name => Main.Tags.AnalogActuatorNames.Contains(name));
                TankCheck("built", built && tags);
                GD.Print($"SELFTEST tank: sensors={string.Join(", ", Main.Tags.AnalogSensorNames)} actuators={string.Join(", ", Main.Tags.AnalogActuatorNames)}");
                Main.Camera.Frame(new Vector3(0.0f, 1.8f, 0.0f), 7.0f);
                Main.Sim.Run();
                _tankPlcOn = true;
                _tankStartedAt = _wallClock;
                _controlsPhase = 1;
                break;
            }

            case 1 when _wallClock - _tankStartedAt >= 25.0:
            {
                GD.Print($"SELFTEST tank: switches={_tankSwitches} level {_tankLowest:0.00}..{_tankHighest:0.00} m, max flow {_tankMaxFlow:0.0} l/s, now {tank?.Level:0.00} m");
                TankCheck("reached high", _tankReachedHigh);
                TankCheck("switches", _tankSwitches >= 4);
                TankCheck("held", _tankLowest >= TankLow - 0.05 && _tankHighest <= TankHigh + 0.05);
                TankCheck("tag follows", tank is not null && Math.Abs(PanelNumber("Tank level") / 100.0 - tank.Level) < 0.02);
                TankCheck("flow", _tankMaxFlow > 100.0);
                SaveScreenshot(string.Empty);

                // The Int tags in the tag table and the I/O list, with the filter "Analoog".
                Ui.Hud hud = Main.Hud;
                hud.SetFilter(FactorySim.Core.Scenes.TagFilter.Analog, string.Empty);
                string[] tankTags = { "Tank fill valve", "Tank discharge valve", "Tank level", "Tank flow" };
                TankCheck("tag table", hud.VisibleTags.Count == 4 && tankTags.All(hud.VisibleTags.Contains));
                TankCheck("io list", hud.IoRowCounts == (2, 2));
                GD.Print($"SELFTEST tank: analog filter shows {string.Join(", ", hud.VisibleTags)}; io list {hud.IoRowCounts}");

                // A forced number is what the PLC reads.
                _tankPlcOn = false;
                hud.SetForceNumber("Tank level", 50);
                _controlsPhase = 2;
                break;
            }

            case 2 when _wallClock - _tankStartedAt >= 25.5:
            {
                TankCheck("forced", PanelNumber("Tank level") == 50);
                Main.Hud.SetForceNumber("Tank level", null);
                Main.Hud.SetFilter(FactorySim.Core.Scenes.TagFilter.All, string.Empty);
                Main.Sim.Reset();
                _controlsPhase = 3;
                break;
            }

            case 3 when _wallClock - _tankStartedAt >= 26.5:
            {
                TankCheck("reset", tank is { Level: 0.0, FillValve: 0, DischargeValve: 0 } && PanelNumber("Tank level") == 0);
                _reported = true;
                GD.Print(_tankOk ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_tankOk ? 0 : 1);
                break;
            }
        }
    }

    private void TankCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST tank {what}: {(ok ? "ok" : "FAILED")}");
        _tankOk &= ok;
    }

    private Vector3 BoxPositionSum()
    {
        Vector3 sum = Vector3.Zero;
        foreach (Node node in GetTree().GetNodesInGroup(Box.Group))
        {
            if (node is Box box && !box.IsQueuedForDeletion())
            {
                sum += box.GlobalPosition;
            }
        }

        return sum;
    }

    private bool Sensor(bool[] sensors, string name)
    {
        for (int i = 0; i < sensors.Length; i++)
        {
            if (Main.Tags.SensorNames[i] == name)
            {
                return sensors[i];
            }
        }

        throw new InvalidOperationException($"No sensor tag '{name}'.");
    }

    private void WriteActuators(bool conveyor, bool pusher, bool emitter)
    {
        var names = Main.Tags.ActuatorNames;
        var values = new bool[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            values[i] = names[i] switch
            {
                TagNames.Conveyor => conveyor,
                TagNames.Pusher => pusher,
                TagNames.Emitter => emitter,
                TagNames.RunLamp => true,
                _ => false,
            };
        }

        Main.Tags.ApplyActuators(values);
    }

    private void Report()
    {
        _reported = true;
        int removed = EndRemover.Count + LaneRemover.Count;
        bool pass;
        if (_mode == "soak")
        {
            // Nothing is pushed, so every box must leave at the end of the belt, and none may pile up.
            pass = _maxFallen == 0 && LaneRemover.Count == 0 && _maxInScene <= 6
                && removed >= Emitter.EmittedCount - 6;
        }
        else if (_mode == "plc")
        {
            // The PLC keeps emitting until Stop, so a few more than the target may have been removed.
            LinkStatistics? stats = Main.Link?.Statistics;
            GD.Print($"SELFTEST link: status={Main.Link?.Status} exchanges={stats?.Exchanges} errors={stats?.Errors} "
                + $"avg={stats?.AverageExchangeMs:0.00}ms max={stats?.MaxExchangeMs:0.0}ms");
            pass = _maxFallen == 0
                && removed >= _amount
                && EndRemover.HighCount == 0
                && LaneRemover.LowCount == 0
                && stats is { Errors: 0 }
                && (!UseTestPlc || Main.TestPlc is { SortedCount: > 0 });     // plan 29: really the built-in one
            if (UseTestPlc)
            {
                GD.Print($"SELFTEST test PLC: port={Main.TestPlc?.Port} sorted={Main.TestPlc?.SortedCount} scans={Main.TestPlc?.Scans}");
                // Verbreken (the same call as the button) stops the server: the port no longer answers.
                int port = Main.TestPlc?.Port ?? 0;
                Main.DisconnectPlc();
                bool closed = Main.TestPlc is null && !PortAnswers(port);
                GD.Print($"SELFTEST test PLC stopped after Verbreken: {(closed ? "ok" : "FAILED")}");
                pass &= closed;
            }
        }
        else
        {
            pass = _maxFallen == 0
                && removed == _amount
                && EndRemover.HighCount == 0
                && LaneRemover.LowCount == 0;
        }

        // The production figures (plan 11) must count exactly what the parts counted themselves.
        bool statsMatch = Main.Stats.EmittedCount == Emitter.EmittedCount
            && Main.Stats.Remover("End remover")?.Count == EndRemover.Count
            && Main.Stats.Remover("Lane remover")?.Count == LaneRemover.Count;
        GD.Print($"SELFTEST stats: emitted={Main.Stats.EmittedCount} removed={Main.Stats.RemovedCount} fallen={Main.Stats.FallenCount} "
            + $"{Main.Stats.PerMinute:0.0}/min -> {(statsMatch ? "ok" : "FAILED")}");
        pass &= statsMatch;
        GD.Print($"SELFTEST elapsed={_elapsed:0.0}s emitted={Emitter.EmittedCount} removed={removed}");
        GD.Print($"SELFTEST end: low={EndRemover.LowCount} high={EndRemover.HighCount}   "
            + $"lane: low={LaneRemover.LowCount} high={LaneRemover.HighCount}");
        GD.Print($"SELFTEST maxInScene={_maxInScene} maxFallen={_maxFallen} fps={Engine.GetFramesPerSecond():0}");
        GD.Print(pass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
        GetTree().Quit(pass ? 0 : 1);
    }
}
