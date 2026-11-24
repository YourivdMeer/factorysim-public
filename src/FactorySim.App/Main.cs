using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.App.Ui;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Api;
using FactorySim.Core.Plc;
using FactorySim.Core.Settings;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App;

/// <summary>
/// The simulator. The scene is not fixed in code: it is built from a scene file that places
/// assets of the library, and every sensor and actuator of those assets runs through the tag store.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>A box whose centre drops below this height has left the belts.</summary>
    public const float FallenBelowY = 0.45f;

    private const string CatalogPath = "res://library/catalog.json";
    private const string DefaultScenePath = ScenePaths.ExamplesFolder + "/mvp_sorting" + ScenePaths.Extension;

    /// <summary>A click this close to a control on screen operates it, in pixels.</summary>
    private const float ClickRadius = 28.0f;

    private readonly SimController _sim = new();
    private AssetCatalog _catalog = null!;
    private Scene _scene = new();
    private TagMap _tagMap = null!;
    private TagStore _tags = null!;
    private SceneView _view = null!;

    private Node3D _world = null!;
    private Node3D _boxes = null!;
    private FloorPart _floor = null!;
    private Hud _hud = null!;
    private ModbusTcpLink? _link;
    private SceneEditor _editor = null!;
    private OperatorControl? _held;
    private bool _ready;
    private int _baseTicksPerSecond = 120;
    private int _baseMaxPhysicsSteps = 8;
    private bool _stepping;
    private readonly TagRecorder _recorder = new();
    private readonly SimResetPulse _resetPulse = new();
    private readonly ProductionStats _stats = new();
    private readonly HashSet<string> _conflictsSeen = new();

    public AssetCatalog Catalog => _catalog;

    /// <summary>The scene that is loaded now.</summary>
    public Scene Scene => _scene;

    public SceneView View => _view;

    public TagStore Tags => _tags;

    public TagMap TagMap => _tagMap;

    public SimController Sim => _sim;

    /// <summary>Parent of everything that moves or senses; it stops when the tree is paused.</summary>
    public Node3D World => _world;

    public Hud Hud => _hud;

    /// <summary>The trace of all tags, tick by tick (plan 10).</summary>
    public TagRecorder Recorder => _recorder;

    /// <summary>Throughput, lead time and on-time of the line (plan 11).</summary>
    public ProductionStats Stats => _stats;

    public Shell Shell { get; private set; } = null!;

    public OrbitCamera Camera { get; private set; } = null!;

    /// <summary>The active PLC link, or null when not connected.</summary>
    public IPlcLink? Link => _link;

    /// <summary>When set, replaces the on-screen Start button (used by the self-test).</summary>
    public bool? StartOverride { get; set; }

    /// <summary>When set, replaces the on-screen Stop button (used by the self-test).</summary>
    public bool? StopOverride { get; set; }

    /// <summary>Raised after a scene has been (re)built.</summary>
    public event Action? SceneChanged;

    public override void _Ready()
    {
        // Main keeps exchanging tags while the simulation is paused; only the world stops.
        ProcessMode = ProcessModeEnum.Always;
        bool selfTest = SelfTest.ParseArguments(OS.GetCmdlineUserArgs()) is not null;
        InstallCrashHandlers(quiet: selfTest || Headless);

        try
        {
            _catalog = AssetCatalog.FromJson(FileAccess.GetFileAsString(CatalogPath));
        }
        catch (CatalogException e)
        {
            ReportStartupFailure($"Cannot load {CatalogPath}: {e.Message}");
            GetTree().Quit(2);
            return;
        }

        Models.Catalog = _catalog;
        BuildEnvironment();
        _view = new SceneView(_world, _boxes, _catalog);
        _view.ItemEmitted += (emitter, box) =>
        {
            box.SpawnTime = _sim.SimTime;
            _stats.Emitted(emitter, _sim.SimTime);
        };
        _view.ItemRemoved += (remover, box) => _stats.Removed(remover, box.SpawnTime, _sim.SimTime);

        // Plan 15: --scene= goes first, then the scene of the last session, then the MVP scene.
        LoadSettings(selfTest);
        string? sceneArgument = UserArgument("--scene=");
        string? startupNotice = null;
        bool fromSettings = false;
        string scenePath = sceneArgument ?? DefaultScenePath;
        if (sceneArgument is null && _settings.ScenePath is { } saved)
        {
            string? savedError = LoadSceneFile(saved);
            if (savedError is null)
            {
                scenePath = saved;
                fromSettings = true;
            }
            else
            {
                startupNotice = $"De scène van de vorige keer kon niet worden geopend ({saved}: {savedError}); de standaardscène is geopend.";
            }
        }

        string? error = fromSettings ? null : LoadSceneFile(scenePath);
        if (error is not null)
        {
            ReportStartupFailure($"Cannot load scene {scenePath}: {error}");
            GetTree().Quit(2);
            return;
        }

        CurrentScenePath = scenePath;

        Shell = new Shell { Name = "Shell" };
        AddChild(Shell);

        _hud = new Hud(Shell, _tagMap, _tags, _sim)
        {
            Name = "Hud",
            GetLink = () => _link,
            ConnectPlc = ConnectPlc,
            DisconnectPlc = DisconnectPlc,
            StartTestPlc = StartTestPlc,
            IsTestPlc = () => _testPlc is not null,
            StartApi = StartApi,
            StopApi = StopApi,
            GetApi = () => _api,
            GetApiToken = () => ApiTokenText,
            SceneName = () => _scene.Name,
            Recorder = _recorder,
            Stats = _stats,
            DrivenTags = () => _view.DrivenTags,
            AboutFolders = AboutFolders,
        };
        _hud.SetTags(_tagMap, _tags, IoList.Build(_scene, _catalog, _tagMap.Entries));
        AddChild(_hud);

        var editor = new SceneEditor(this) { Name = "SceneEditor" };
        _editor = editor;
        AddChild(editor);
        _hud.PartClicked += itemId =>
        {
            editor.Select(itemId);
            FocusOn(itemId);
            if (!editor.DesignMode)
            {
                editor.FlashItem(itemId);          // the part lights up while simulating
            }
        };
        BuildViewButtons();
        Shell.StatusDetails.Pressed += OpenLogFile;

        _sim.StateChanged += state => GetTree().Paused = state != SimState.Running;
        _baseTicksPerSecond = Engine.PhysicsTicksPerSecond;
        _baseMaxPhysicsSteps = Engine.MaxPhysicsStepsPerFrame;
        _sim.TimeScaleChanged += ApplyTimeScale;
        _sim.ResetRequested += ResetScene;
        GetTree().Paused = true;
        _ready = true;
        StartApiIfRequested();

        if (_settingsPath is not null)
        {
            // The camera only when it was saved with this scene; with --scene= or the fallback the view stays as it starts.
            AppSettings start = sceneArgument is null ? _settings : _settings with { Camera = null };
            ApplySettings(start, includeWindow: true);
            if (fromSettings && start.Camera is null)
            {
                FrameScene();
            }

            if (startupNotice is not null)
            {
                editor.ShowMessage(startupNotice, isError: true);
            }
        }

        SetUpWelcome();
        StartSelfTestIfRequested(editor);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready)
        {
            return;
        }

        // One tick forward while paused: the world runs this physics frame only.
        if (_stepping)
        {
            _stepping = false;
            GetTree().Paused = _sim.State != SimState.Running;
        }

        if (_sim.TakeStep())
        {
            _stepping = true;
            GetTree().Paused = false;
        }

        double timeBefore = _sim.SimTime;
        _sim.Advance(1.0 / _baseTicksPerSecond);
        bool ticked = _sim.SimTime != timeBefore;

        // 1. Sensors measured during the previous tick go to the store...
        _tags.SetSensor(SceneTags.StartButton, StartOverride ?? _hud.StartPressed);
        _tags.SetSensor(SceneTags.StopButton, StopOverride ?? _hud.StopPressed);
        _tags.SetSensor(SceneTags.SimRunning, _sim.IsRunning);
        // Plan 14, only when the scene switches them on. The reset pulse runs on real time:
        // after a reset the simulation stands still, and delta scales with the time scale.
        if (_scene.SimTags.Contains(SceneTags.SimPaused))
        {
            _tags.SetSensor(SceneTags.SimPaused, _sim.State == SimState.Paused);
        }

        if (_scene.SimTags.Contains(SceneTags.SimReset))
        {
            _tags.SetSensor(SceneTags.SimReset, _resetPulse.IsHigh(Time.GetTicksMsec() / 1000.0));
        }

        foreach ((string tag, Func<bool> read) in _view.Sensors)
        {
            _tags.SetSensor(tag, read());
        }

        foreach ((string tag, Func<double> read) in _view.AnalogSensors)
        {
            _tags.SetSensorValue(tag, read());
        }

        // 2. ...which publishes them to the PLC link and latches the PLC's latest outputs...
        _tags.EndTick();
        if (ticked)
        {
            _recorder.Record(_sim.SimTime, _tags);
        }

        // 3. ...that the parts then act on during this tick.
        foreach ((string tag, Action<bool> write) in _view.Actuators)
        {
            write(_tags.GetActuator(tag));
        }

        foreach ((string tag, Action<int> write) in _view.AnalogActuators)
        {
            write(_tags.GetActuatorNumber(tag));
        }

        // 4. Production figures, in simulation time: nothing adds up while paused.
        if (ticked)
        {
            CountFallen();
            _stats.Tick(_sim.SimTime, _sim.SimTime - timeBefore,
                _view.Actuators.Select(actuator => (actuator.Tag, _tags.GetActuator(actuator.Tag))));
        }
    }

    /// <summary>Reports every item that dropped below <see cref="FallenBelowY"/> to the figures, once.</summary>
    private void CountFallen()
    {
        foreach (Node child in _boxes.GetChildren())
        {
            if (child is Box { FallenCounted: false } box && !box.IsQueuedForDeletion() && box.GlobalPosition.Y < FallenBelowY)
            {
                box.FallenCounted = true;
                _stats.Fallen(_sim.SimTime);
            }
        }
    }

    /// <summary>Clears the production figures and lists the removers of the scene with zeros.</summary>
    public void ResetStats()
    {
        _stats.Reset();
        foreach ((string name, _) in _view.Removers)
        {
            _stats.AddRemover(name);
        }
    }

    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
        }

        PollSettings();
        UpdateWindowTitle();
        ShowNewErrors();
        _apiQueue.Pump();                                // plan 21: work from the test API thread

        Camera.FreeRect = Shell.FreeRect;
        (int inScene, int fallen) = CountBoxes();
        string removed = string.Join("   ", _view.Removers.Select(r => $"{r.Name}: {r.Remover.Count}"));
        _hud.Info = $"Gespawnd: {_view.Emitters.Sum(e => e.EmittedCount)}   {removed}   "
            + $"In scène: {inScene}   Gevallen: {fallen}   {Engine.GetFramesPerSecond():0} fps";
        ShowConflicts();
    }

    /// <summary>Two outputs that may not be on together and are (plan 12), as shown in the status bar; empty when none.</summary>
    public string ConflictText { get; private set; } = string.Empty;

    /// <summary>One line in the status bar while a conflict lasts, and one console line when it starts.</summary>
    private void ShowConflicts()
    {
        var active = new List<string>();
        foreach ((string name, Func<bool> isOn, string text) in _view.Conflicts)
        {
            if (!isOn())
            {
                _conflictsSeen.Remove(name);
                continue;
            }

            active.Add(text);
            if (_conflictsSeen.Add(name))
            {
                GD.Print($"PLC-fout: {text} (t = {_sim.SimTime:0.000} s)");
            }
        }

        // The editor uses the same label for its warnings in design mode (and clears it when simulating), so
        // while simulating write it whenever it differs, not only when the conflicts change.
        string line = string.Join("   ·   ", active);
        if (!_editor.DesignMode && Shell.StatusWarning.Text != line)
        {
            Shell.StatusWarning.Text = line;
        }

        ConflictText = line;
    }

    // ---------------------------------------------------------------- camera

    /// <summary>Puts a scene item in the middle of the view.</summary>
    public void FocusOn(string itemId)
    {
        if (_scene.Find(itemId) is not { } item || _catalog.Find(item.Asset) is not { } asset)
        {
            return;
        }

        float size = (float)Math.Max(asset.Size[0], Math.Max(asset.Size[1], asset.Size[2]));
        Camera.Frame(new Vector3((float)item.X, (float)(item.Y + (asset.BoundsMin[1] + asset.BoundsMax[1]) / 2.0), (float)item.Z),
            Mathf.Max(3.0f, size * 2.2f));
    }

    /// <summary>The floor as it is now: length along X and width along Z, in metres.</summary>
    public (float Length, float Width) FloorSize => _floor.Size;

    /// <summary>Puts the whole scene in view.</summary>
    public void FrameScene()
    {
        if (_scene.Items.Count == 0)
        {
            Camera.Frame(new Vector3(0.0f, 0.8f, 0.0f), Mathf.Max(9.0f, (float)Math.Max(_scene.Floor.Length, _scene.Floor.Width) * 0.65f));
            return;
        }

        float minX = (float)_scene.Items.Min(item => item.X);
        float maxX = (float)_scene.Items.Max(item => item.X);
        float minZ = (float)_scene.Items.Min(item => item.Z);
        float maxZ = (float)_scene.Items.Max(item => item.Z);
        float span = Mathf.Max(maxX - minX, maxZ - minZ);
        Camera.Frame(new Vector3((minX + maxX) / 2.0f, 0.8f, (minZ + maxZ) / 2.0f), Mathf.Max(5.0f, span * 1.1f + 3.0f));
    }

    /// <summary>Focus on the selected item, or on the whole scene when nothing is selected.</summary>
    public void FocusSelection()
    {
        if (_editor.SelectedId is { } id)
        {
            FocusOn(id);
        }
        else
        {
            FrameScene();
        }
    }

    private void BuildViewButtons()
    {
        (string Text, string Tooltip, Action Action)[] buttons =
        {
            ("Focus", "Selectie in beeld (F)", FocusSelection),
            ("Alles", "Hele scène in beeld (Home)", FrameScene),
            ("Boven", "Van bovenaf", () => Camera.SetView(0.0f, -89.0f)),
            ("Voor", "Vooraanzicht", () => Camera.SetView(0.0f, -8.0f)),
            ("Zij", "Zijaanzicht", () => Camera.SetView(-90.0f, -8.0f)),
            ("Iso", "Schuin van boven", () => Camera.SetView(-35.0f, -32.0f)),
        };
        foreach ((string text, string tooltip, Action action) in buttons)
        {
            Shell.ViewButtons.AddChild(Shell.MakeButton(text, action, tooltip));
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ready)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (Click(click.Position, click.Pressed))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event is not InputEventKey { Echo: false, Pressed: true } key)
        {
            return;
        }

        switch (key.PhysicalKeycode)
        {
            case Key.F when !key.IsCommandOrControlPressed():
                FocusSelection();
                break;
            case Key.Home:
                FrameScene();
                break;
            case Key.Key1:
                _hud.ToggleForce(TagNames.Conveyor);
                break;
            case Key.Key2:
                _hud.ToggleForce(TagNames.Pusher);
                break;
            case Key.Key3:
                _hud.ToggleForce(TagNames.Emitter);
                break;
            case Key.Equal or Key.KpAdd:
                _sim.Faster();
                break;
            case Key.Minus or Key.KpSubtract:
                _sim.Slower();
                break;
            case Key.Period:
                _sim.Step();
                break;
        }
    }

    public override void _ExitTree()
    {
        StopApi();
        WriteSettingsOnExit();
        DisconnectPlc();
        RemoveCrashHandlers();
    }

    /// <summary>
    /// Engine.TimeScale alone keeps the ticks per real second and makes each step longer (measured, see
    /// TROUBLESHOOTING.md). Scaling the tick rate along keeps every step at 1/120 s of simulation time.
    /// </summary>
    private void ApplyTimeScale(double scale)
    {
        Engine.TimeScale = scale;
        Engine.PhysicsTicksPerSecond = (int)Math.Round(_baseTicksPerSecond * scale);
        Engine.MaxPhysicsStepsPerFrame = scale > 1.0
            ? (int)Math.Ceiling(_baseMaxPhysicsSteps * scale)
            : _baseMaxPhysicsSteps;
    }

    // ---------------------------------------------------------------- scenes

    /// <summary>Reads a scene file (res://, user:// or a file system path). Returns an error text, or null.</summary>
    public string? LoadSceneFile(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            return "het bestand bestaat niet";
        }

        try
        {
            return ApplyScene(Scene.FromJson(FileAccess.GetFileAsString(path)));
        }
        catch (SceneException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Makes this the current scene: stops the simulation, rebuilds the scene from the items
    /// and derives the tags. Returns an error text when the scene cannot run, or null.
    /// </summary>
    public string? ApplyScene(Scene scene)
    {
        IReadOnlyList<TagMapEntry> entries;
        try
        {
            entries = SceneTags.Build(scene, _catalog);
        }
        catch (SceneException e)
        {
            return e.Message;
        }

        DisconnectPlc();
        if (_ready)
        {
            _sim.Reset();
        }

        foreach (Node child in _boxes.GetChildren())
        {
            child.QueueFree();
        }

        _held = null;
        _scene = scene;
        _floor?.Resize((float)scene.Floor.Length, (float)scene.Floor.Width);
        scene.Tags.Clear();
        scene.Tags.AddRange(entries);
        _view.Build(scene);
        _tagMap = new TagMap(entries);
        _tags = new TagStore(_tagMap.ToDefinitions());
        _tagSource = new TagSource(_tags, _tagMap);      // one reference for the test API thread
        _recorder.Clear();
        ResetStats();
        _hud?.SetTags(_tagMap, _tags, IoList.Build(scene, _catalog, entries));
        SceneChanged?.Invoke();
        return null;
    }

    /// <summary>Writes the current scene to a file (user:// or a file system path). Returns an error text, or null.</summary>
    public string? SaveSceneFile(string path)
    {
        try
        {
            string target = path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
            string? folder = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            System.IO.File.WriteAllText(target, _scene.ToJson());
            return null;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return e.Message;
        }
    }

    // ------------------------------------------------------------------- PLC

    /// <summary>Starts exchanging tags with a Modbus TCP server; replaces any existing link.</summary>
    public void ConnectPlc(ModbusLinkOptions options)
    {
        DisconnectPlc();
        ConnectLink(options);
    }

    private void ConnectLink(ModbusLinkOptions options)
    {
        _link = new ModbusTcpLink(_tags, _tagMap, options);
        _linkOptions = options;
        _link.Start();
    }

    /// <summary>Drops the link, and stops the built-in test PLC when it runs (plan 29).</summary>
    public void DisconnectPlc()
    {
        if (_link is null)
        {
            StopTestPlc();
            return;
        }

        _link.Dispose();
        _link = null;
        StopTestPlc();
        _linkOptions = null;

        // Without a PLC nothing commands the actuators any more: drop them all.
        _tags.ApplyActuators(new bool[_tags.ActuatorNames.Count]);
        _tags.ApplyActuatorNumbers(new int[_tags.AnalogActuatorNames.Count]);
    }

    // -------------------------------------------------------------- controls

    /// <summary>The knob of a potentiometer drawn under a point on screen, or null.</summary>
    public Potentiometer? PotentiometerAt(Vector2 screen)
    {
        Potentiometer? nearest = null;
        float best = ClickRadius;
        foreach (Potentiometer pot in _view.Potentiometers)
        {
            if (Camera.IsPositionBehind(pot.PickPoint))
            {
                continue;
            }

            float distance = Camera.UnprojectPosition(pot.PickPoint).DistanceTo(screen);
            if (distance < best)
            {
                best = distance;
                nearest = pot;
            }
        }

        return nearest;
    }

    /// <summary>The mouse wheel over a potentiometer turns it while simulating; anywhere else it zooms.</summary>
    private bool TurnKnobAt(Vector2 screen, int notches)
    {
        if (_editor.DesignMode || PotentiometerAt(screen) is not { } pot)
        {
            return false;
        }

        pot.Step(notches);
        return true;
    }

    /// <summary>The button, emergency stop or selector drawn under a point on screen, or null.</summary>
    public OperatorControl? ControlAt(Vector2 screen)
    {
        OperatorControl? nearest = null;
        float best = ClickRadius;
        foreach (OperatorControl control in _view.Controls)
        {
            Vector3 position = control.PickPoint;
            if (Camera.IsPositionBehind(position))
            {
                continue;
            }

            float distance = Camera.UnprojectPosition(position).DistanceTo(screen);
            if (distance < best)
            {
                best = distance;
                nearest = control;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Left mouse button went down or up at a point on screen. Pressing operates the control
    /// under the mouse; letting go releases the control that was pressed, wherever the mouse
    /// is by then. Returns true when a control took the click. Does nothing in design mode.
    /// </summary>
    public bool Click(Vector2 screen, bool pressed)
    {
        if (!pressed)
        {
            OperatorControl? held = _held;
            _held = null;
            if (held is null || !IsInstanceValid(held))
            {
                return false;
            }

            held.Release();
            return true;
        }

        if (_editor.DesignMode)
        {
            return false;
        }

        if (PotentiometerAt(screen) is { } pot)
        {
            // Left of the knob's centre turns it down, right of it turns it up.
            pot.Step(screen.X < Camera.UnprojectPosition(pot.PickPoint).X ? -1 : 1);
            return true;
        }

        if (ControlAt(screen) is not { } control)
        {
            InspectAt(screen);
            return false;
        }

        control.Press();
        _held = control;
        return true;
    }

    /// <summary>
    /// A click while simulating on something that is not a control: the tables show only the tags of the
    /// part that was clicked and the part lights up; a click on empty floor shows all tags again.
    /// </summary>
    public void InspectAt(Vector2 screen)
    {
        SceneItem? item = _editor.ItemAtScreen(screen);
        _hud.ShowOnly(item?.Id);
        if (item is not null)
        {
            _editor.FlashItem(item.Id);
        }
    }

    // ----------------------------------------------------------------- scene

    /// <summary>Counts the boxes on the scene and how many of them left the belts.</summary>
    public (int InScene, int Fallen) CountBoxes()
    {
        int inScene = 0;
        int fallen = 0;
        foreach (Node child in _boxes.GetChildren())
        {
            if (child is not Box box || box.IsQueuedForDeletion())
            {
                continue;
            }

            inScene++;
            if (box.GlobalPosition.Y < FallenBelowY)
            {
                fallen++;
            }
        }

        return (inScene, fallen);
    }

    private void ResetScene()
    {
        _resetPulse.Trigger(Time.GetTicksMsec() / 1000.0);
        foreach (Node child in _boxes.GetChildren())
        {
            child.QueueFree();
        }

        _hud.ClearForces();
        _tags.ResetValues();
        _recorder.Clear();
        _view.Reset();
        ResetStats();
    }

    private void StartSelfTestIfRequested(SceneEditor editor)
    {
        string[] args = OS.GetCmdlineUserArgs();
        var request = SelfTest.ParseArguments(args);
        if (request is null)
        {
            return;
        }

        AddChild(new SelfTest(request.Value.Mode, request.Value.Amount, request.Value.Screenshot)
        {
            Name = "SelfTest",
            Main = this,
            Editor = editor,
            PlcPort = SelfTest.ParsePlcPort(args),
            UseTestPlc = SelfTest.ParseTestPlc(args),
            ScreenshotAtSeconds = SelfTest.ParseScreenshotTime(args),
        });
    }

    private static string? UserArgument(string prefix)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private void BuildEnvironment()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.13f, 0.15f, 0.18f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.80f, 0.90f),
                AmbientLightEnergy = 0.6f,
            },
        });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55.0f, -35.0f, 0.0f),
            ShadowEnabled = true,
            LightEnergy = 1.1f,
        });

        Camera = new OrbitCamera
        {
            Pivot = new Vector3(0.3f, 0.8f, 0.6f),
            Distance = 7.0f,
            Current = true,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(Camera);
        Camera.WheelOverride = TurnKnobAt;

        // Everything that moves or senses lives under World, which stops when the tree is paused.
        _world = new Node3D { Name = "World", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(_world);

        _floor = new FloorPart();
        _world.AddChild(_floor);
        _floor.Resize((float)Scene.DefaultFloor.Length, (float)Scene.DefaultFloor.Width);

        _boxes = new Node3D { Name = "Boxes" };
        _world.AddChild(_boxes);
    }
}
