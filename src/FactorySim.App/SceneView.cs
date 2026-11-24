using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Robot;
using Godot;

namespace FactorySim.App;

/// <summary>
/// The nodes of one scene: builds a part for every item and records how each tag
/// reads from or writes to those parts. Functional assets get their simulator part;
/// all other assets are placed as decoration without behaviour.
/// </summary>
public sealed class SceneView
{
    private readonly Node3D _root;
    private readonly Node3D _boxes;
    private readonly AssetCatalog _catalog;
    private readonly Dictionary<string, Node3D> _nodesByItemId = new();
    private readonly Dictionary<string, Node3D> _nodesByName = new();
    private readonly List<Action> _resets = new();
    private Dictionary<string, SensorLink> _links = new();

    public SceneView(Node3D root, Node3D boxes, AssetCatalog catalog)
    {
        _root = root;
        _boxes = boxes;
        _catalog = catalog;
    }

    /// <summary>Sensor tags and how to read each one from its part.</summary>
    public List<(string Tag, Func<bool> Read)> Sensors { get; } = new();

    /// <summary>Actuator tags and how to apply each one to its part.</summary>
    public List<(string Tag, Action<bool> Write)> Actuators { get; } = new();

    /// <summary>Int sensor tags and how to read each one: the value in the simulation, which the store scales.</summary>
    public List<(string Tag, Func<double> Read)> AnalogSensors { get; } = new();

    /// <summary>Int actuator tags and how to apply each one: the number on the line.</summary>
    public List<(string Tag, Action<int> Write)> AnalogActuators { get; } = new();

    public List<Emitter> Emitters { get; } = new();

    public List<(string Name, Remover Remover)> Removers { get; } = new();

    /// <summary>Actuator tags of driven parts (belts, bends, wheel sorters, turntables, lifts), for the on-time in the tab Cijfers.</summary>
    public List<string> DrivenTags { get; } = new();

    /// <summary>
    /// Errors in the PLC program that the status bar shows while they last (plan 12): a two-way belt with
    /// forward and reverse on, a bistable pusher with both coils on. (item name, is it on now, text).
    /// </summary>
    public List<(string Name, Func<bool> Active, string Text)> Conflicts { get; } = new();

    /// <summary>An emitter made an item: (emitter name, item). Survives a rebuild of the scene.</summary>
    public event Action<string, Box>? ItemEmitted;

    /// <summary>A remover took an item: (remover name, item). Survives a rebuild of the scene.</summary>
    public event Action<string, Box>? ItemRemoved;

    /// <summary>The controls the operator can click.</summary>
    public List<OperatorControl> Controls { get; } = new();

    /// <summary>The potentiometers the operator can turn.</summary>
    public List<Potentiometer> Potentiometers { get; } = new();

    /// <summary>Items whose asset the library does not have; they are skipped.</summary>
    public List<string> Skipped { get; } = new();

    public void Build(Scene scene)
    {
        Clear();
        _links = SensorPairing.Links(scene, _catalog).ToDictionary(link => link.SensorId);
        foreach (SceneItem item in scene.Items)
        {
            CatalogAsset? asset = _catalog.Find(item.Asset);
            if (asset is null)
            {
                Skipped.Add(item.Name);
                continue;
            }

            Node3D node = BuildItem(item, asset);
            node.Name = SafeNodeName(item.Id);
            node.SetMeta("item_id", item.Id);
            _root.AddChild(node);
            _nodesByItemId[item.Id] = node;
            _nodesByName[item.Name] = node;
            AfterAdded(item, asset, node);
            AddSupports(item, asset, node);
        }
    }

    public void Clear()
    {
        foreach (Node3D node in _nodesByItemId.Values)
        {
            node.QueueFree();
        }

        _nodesByItemId.Clear();
        _nodesByName.Clear();
        _resets.Clear();
        Sensors.Clear();
        Actuators.Clear();
        AnalogSensors.Clear();
        AnalogActuators.Clear();
        Emitters.Clear();
        Removers.Clear();
        DrivenTags.Clear();
        Conflicts.Clear();
        Controls.Clear();
        Potentiometers.Clear();
        Skipped.Clear();
    }

    /// <summary>Returns every part to its rest state (scene reset).</summary>
    public void Reset()
    {
        foreach (Action reset in _resets)
        {
            reset();
        }
    }

    public Node3D? NodeOf(string itemId) => _nodesByItemId.GetValueOrDefault(itemId);

    /// <summary>Finds the part of an item by the item's name, e.g. the Remover called "End remover".</summary>
    public T? Find<T>(string itemName)
        where T : Node3D => _nodesByName.GetValueOrDefault(itemName) as T;

    private Node3D BuildItem(SceneItem item, CatalogAsset asset)
    {
        var position = new Vector3((float)item.X, (float)item.Y, (float)item.Z);
        var rotation = new Vector3(0.0f, (float)item.Yaw, 0.0f);

        switch (asset.Behaviour)
        {
            case AssetBehaviours.Conveyor:
            {
                var conveyor = new Conveyor
                {
                    ModelName = asset.Id,
                    Length = (float)item.Param("length", asset, 2.0),
                    Width = (float)item.Param("width", asset, 0.8),
                    Speed = (float)item.Param("speed", asset, 0.6),
                    Rise = (float)item.Param("rise", asset, 0.0),
                    Reverse = item.Param(SceneTags.Reverse, asset, 0.0) >= 0.5,
                    Weighs = item.Param(SceneTags.Weighs, asset, 0.0) >= 0.5,
                    // The item's height is the transport surface; the node sits at the collider centre.
                    Position = position - new Vector3(0.0f, Conveyor.SurfaceOffset, 0.0f),
                    RotationDegrees = rotation,
                };
                return conveyor;
            }

            case AssetBehaviours.Pusher:
                return new Pusher((float)item.Param("stroke", asset, 0.88), (float)item.Param("speed", asset, 1.0),
                    item.Param(SceneTags.Bistable, asset, 0.0) > 0.5)
                {
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.PhotoEye:
            case AssetBehaviours.Retroreflective:
            case AssetBehaviours.LightArray:
            case AssetBehaviours.Vision:
            {
                // A sensor that needs a partner looks as far as the partner and sees nothing without one.
                double range = item.Param("range", asset, 1.0);
                bool paired = true;
                if (SensorPairing.TargetBehaviourOf(asset.Behaviour) is not null)
                {
                    paired = _links.TryGetValue(item.Id, out SensorLink? link);
                    range = link?.Distance ?? range;
                }

                var eye = new PhotoEye
                {
                    ModelName = asset.Id,
                    Range = (float)range,
                    Active = paired,
                    MetalOnly = item.Param("metalOnly", asset, 0.0) > 0.5,
                    Beams = (int)item.Param("beams", asset, 1.0),
                    BeamSpacing = (float)item.Param("beamSpacing", asset, 0.05),
                    Position = position,
                    RotationDegrees = rotation,
                };
                if (item.Param("post", asset, 1.0) > 0.5)
                {
                    // Post from the floor, standing just behind the sensor head.
                    Node3D post = Models.Instantiate("sensor_post");
                    post.Name = "Post";
                    post.Position = new Vector3(0.0f, -(float)item.Y, -0.095f);
                    eye.AddChild(post);
                }

                return eye;
            }

            case AssetBehaviours.Emitter:
            {
                var emitter = new Emitter(item.Param("interval", asset, 2.5), (int)item.Param("seed", asset, 1.0),
                    Math.Clamp(item.Param("highRatio", asset, 0.5), 0.0, 1.0))
                {
                    BoxParent = _boxes,
                    ModelName = asset.Id,
                    Position = position,
                    RotationDegrees = rotation,
                };
                // The item the user chose for this emitter, otherwise the one of its asset, otherwise the two standard boxes.
                if (EmitterItems.Resolve(item, asset, _catalog) is { } emitted)
                {
                    (double length, double width) = EmitterItems.VolumeFor(emitted);
                    emitter.ItemModel = emitted.Id;
                    emitter.ItemSize = new Vector3((float)emitted.Size[0], (float)emitted.Size[1], (float)emitted.Size[2]);
                    emitter.ItemMass = (float)EmitterItems.MassFor(item, asset, emitted);
                    emitter.VolumeSize = new Vector3((float)length, 0.6f, (float)width);
                }

                return emitter;
            }

            case AssetBehaviours.Remover:
                return new Remover
                {
                    ModelName = asset.Id,
                    Size = new Vector3((float)item.Param("depth", asset, 0.6), 1.2f, (float)item.Param("width", asset, 1.2)),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Rail:
                return BuildRail(item, asset, position, rotation);

            case AssetBehaviours.Stop:
                return new StopGate((float)item.Param("stroke", asset, 0.12), (float)item.Param("speed", asset, 0.6))
                {
                    ModelName = asset.Id,
                    BladeWidth = (float)item.Param("bladeWidth", asset, 0.72),
                    BladeHeight = (float)item.Param("bladeHeight", asset, 0.14),
                    BladeCentre = (float)item.Param("bladeCentre", asset, -0.075),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Clamp:
                return new PositioningBars
                {
                    ModelName = asset.Id,
                    OpenHalfGap = (float)item.Param("openHalfGap", asset, 0.52),
                    Stroke = (float)item.Param("stroke", asset, 0.42),
                    Speed = (float)item.Param("speed", asset, 0.3),
                    BladeLength = (float)item.Param("bladeLength", asset, 0.5),
                    BladeHeight = (float)item.Param("bladeHeight", asset, 0.16),
                    BladeThickness = (float)item.Param("bladeThickness", asset, 0.04),
                    BladeCentre = (float)item.Param("bladeCentre", asset, 0.12),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Passive:
                return new PassivePart
                {
                    ModelName = asset.Id,
                    Colliders = asset.Colliders ?? Array.Empty<ColliderSpec>(),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Sign:
                return new Sign
                {
                    ModelName = asset.Id,
                    OnFloor = !asset.Parts.Contains("Panel"),
                    Text = item.Text ?? item.Name,
                    Size = SignText.SizeOf(item.Param(SignText.SizeParam, asset, (double)SignSize.Medium)),
                    Colliders = asset.Colliders ?? Array.Empty<ColliderSpec>(),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Curve:
                return new CurveConveyor
                {
                    ModelName = asset.Id,
                    Radius = (float)item.Param("radius", asset, 1.0),
                    Width = (float)item.Param("width", asset, 0.8),
                    Speed = (float)item.Param("speed", asset, 0.6),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.WheelSorter:
                return new WheelSorter
                {
                    ModelName = asset.Id,
                    Length = (float)item.Param("length", asset, 1.0),
                    Width = (float)item.Param("width", asset, 0.8),
                    Speed = (float)item.Param("speed", asset, 0.6),
                    Angle = (float)item.Param("angle", asset, 45.0),
                    Lift = (float)item.Param("lift", asset, 0.012),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Turntable:
                return new Turntable((float)item.Param("angle", asset, 90.0), (float)item.Param("turnSpeed", asset, 60.0))
                {
                    ModelName = asset.Id,
                    Radius = (float)item.Param("radius", asset, 0.9),
                    Speed = (float)item.Param("speed", asset, 0.45),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Palletizer:
                return new Palletizer(
                    (float)item.Param("pushStroke", asset, 0.37), (float)item.Param("pushSpeed", asset, 0.4),
                    (float)item.Param("leafStroke", asset, 0.5), (float)item.Param("leafSpeed", asset, 1.0),
                    (float)item.Param("tableTravel", asset, 1.25), (float)item.Param("tableSpeed", asset, 0.3))
                {
                    ModelName = asset.Id,
                    DeckHeight = (float)item.Param("deck", asset, 1.9),
                    BaseHeight = (float)item.Param("base", asset, 0.5),
                    TableLength = (float)item.Param("tableLength", asset, 2.5),
                    TableWidth = (float)item.Param("tableWidth", asset, 1.0),
                    FeedSpeed = (float)item.Param("feedSpeed", asset, 0.4),
                    RollSpeed = (float)item.Param("rollSpeed", asset, 0.45),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Machining:
                return new MachiningCenter(
                    (float)item.Param("entryAngle", asset, 127.864), (float)item.Param("swingSpeed", asset, 120.0),
                    (float)item.Param("liftTime", asset, 0.4), (float)item.Param("machineTime", asset, 4.0))
                {
                    ModelName = asset.Id,
                    BoxParent = _boxes,
                    Surface = (float)item.Param("surface", asset, 0.8),
                    BaseX = (float)item.Param("baseX", asset, -0.55),
                    Reach = (float)item.Param("reach", asset, 0.76),
                    Lane = (float)item.Param("lane", asset, 0.6),
                    LaneWidth = (float)item.Param("laneWidth", asset, 0.8),
                    StopX = (float)item.Param("stopX", asset, -0.8715),
                    TipHeight = (float)item.Param("tipHeight", asset, 1.1),
                    Lift = (float)item.Param("lift", asset, 0.15),
                    SpindleHeight = (float)item.Param("spindleHeight", asset, 1.35),
                    DeckSpeed = (float)item.Param("deckSpeed", asset, 0.4),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.StackerCrane:
            {
                var positions = new StackerPositions(
                    asset.Param("columnSpacing", 1.5), asset.Param("levelHeight", 1.2), asset.Param("firstLevelTop", 0.6),
                    asset.Param("transferTop", 0.5), asset.Param("clearance", 0.03));
                return new StackerStation(positions, item.Param(SceneTags.StackerJog, asset, 0.0) >= 0.5,
                    item.Param("driveSpeed", asset, 0.5), item.Param("hoistSpeed", asset, 0.4),
                    asset.Param("forkStroke", 1.4), item.Param("forkSpeed", asset, 0.7),
                    asset.Param("liftStroke", 0.1), item.Param("liftSpeed", asset, 0.1))
                {
                    ModelName = asset.Id,
                    ForkLength = (float)asset.Param("forkLength", 1.5),
                    ForkSpacing = (float)asset.Param("forkSpacing", 0.5),
                    ForkWidth = (float)asset.Param("forkWidth", 0.1),
                    ForkThickness = (float)asset.Param("forkThickness", 0.04),
                    Position = position,
                    RotationDegrees = rotation,
                };
            }

            case AssetBehaviours.Rack:
                return new RackStation
                {
                    ModelName = asset.Id,
                    ColumnSpacing = (float)asset.Param("columnSpacing", 1.5),
                    LevelHeight = (float)asset.Param("levelHeight", 1.2),
                    FirstLevelTop = (float)asset.Param("firstLevelTop", 0.6),
                    BeamSpacing = (float)asset.Param("beamSpacing", 0.68),
                    BeamWidth = (float)asset.Param("beamWidth", 0.05),
                    BeamHeight = (float)asset.Param("beamHeight", 0.1),
                    Depth = (float)asset.Param("depth", 1.3),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Elevator:
                return new Elevator((float)item.Param("travel", asset, 3.0), (float)item.Param("speed", asset, 0.5))
                {
                    ModelName = asset.Id,
                    BaseHeight = (float)item.Param("base", asset, 0.5),
                    Length = (float)item.Param("length", asset, 1.6),
                    Width = (float)item.Param("width", asset, 1.4),
                    RollSpeed = (float)item.Param("rollSpeed", asset, 0.45),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Robot:
                return new RobotArm(
                    RobotGeometry.FromParams((name, fallback) => item.Param(name, asset, fallback)),
                    item.Param("speed", asset, 40.0),
                    (float)item.Param("pedestal", asset, 0.75))
                {
                    ModelName = asset.Id,
                    ModelPedestal = (float)asset.Param("pedestal", 0.75),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.PickPlace:
                return new PickAndPlace(
                    (float)item.Param("strokeX", asset, 1.0),
                    (float)item.Param(SceneTags.StrokeY, asset, 0.0),
                    (float)item.Param("strokeZ", asset, 0.7),
                    (float)item.Param("speed", asset, 1.0),
                    (float)item.Param("speedZ", asset, 0.6))
                {
                    ModelName = asset.Id,
                    RestX = (float)item.Param("restX", asset, -0.5),
                    TipHeight = (float)item.Param("tipHeight", asset, 1.22),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.ArmSorter:
                return new ArmSorter((float)item.Param("angle", asset, 45.0), (float)item.Param("swingSpeed", asset, 120.0))
                {
                    ModelName = asset.Id,
                    Length = (float)item.Param("length", asset, 1.2),
                    BeltSpeed = (float)item.Param("speed", asset, 0.6),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Encoder:
                return new Encoder
                {
                    ModelName = asset.Id,
                    PulseDistance = (float)item.Param("pulseDistance", asset, 0.05),
                    WheelRadius = (float)asset.Param("wheelRadius", 0.04),
                    Position = position,
                    RotationDegrees = rotation,
                };

            case AssetBehaviours.Button:
            {
                var control = new OperatorControl(
                    latching: item.Param("latching", asset, 0.0) > 0.5,
                    normallyClosed: item.Param("normallyClosed", asset, 0.0) > 0.5)
                {
                    ModelName = asset.Id,
                    Turns = item.Param("knob", asset, 0.0) > 0.5,
                    Swings = item.Param("door", asset, 0.0) > 0.5,
                    SwingAngle = (float)item.Param("swing", asset, 90.0),
                    PickOffset = new Vector3(0.0f, (float)item.Param("pickHeight", asset, 0.0), 0.0f),
                    Position = position,
                    RotationDegrees = rotation,
                };
                AddPost(control, item, asset);
                return control;
            }

            case AssetBehaviours.Lamp:
            {
                var lamp = new IndicatorLamp
                {
                    ModelName = asset.Id,
                    LampParts = SceneTags.LampParts(asset).Select(part => part.Part).ToArray(),
                    Position = position,
                    RotationDegrees = rotation,
                };
                AddPost(lamp, item, asset);
                return lamp;
            }

            case AssetBehaviours.Potentiometer:
            {
                var pot = new Potentiometer((int)item.Param("start", asset, 0.0))
                {
                    ModelName = asset.Id,
                    Position = position,
                    RotationDegrees = rotation,
                };
                AddPost(pot, item, asset);
                return pot;
            }

            case AssetBehaviours.Display:
            {
                var display = new DigitalDisplay
                {
                    ModelName = asset.Id,
                    Decimals = (int)item.Param("decimals", asset, 0.0),
                    Position = position,
                    RotationDegrees = rotation,
                };
                AddPost(display, item, asset);
                return display;
            }

            case AssetBehaviours.Siren:
            {
                var siren = new Siren { ModelName = asset.Id, Position = position, RotationDegrees = rotation };
                AddPost(siren, item, asset);
                return siren;
            }

            case AssetBehaviours.Tank:
                return new Tank
                {
                    ModelName = asset.Id,
                    TankHeight = asset.Param("height", 3.0),
                    Diameter = asset.Param("diameter", 2.0),
                    MaxInflow = item.Param("maxInflow", asset, 0.2),
                    MaxOutflow = item.Param("maxOutflow", asset, 0.2),
                    Position = position,
                    RotationDegrees = rotation,
                };

            default:
            {
                // Visual only: no collider and no tags.
                var holder = new Node3D { Position = position, RotationDegrees = rotation };
                holder.AddChild(Models.Instantiate(asset.Id));
                return holder;
            }
        }
    }

    /// <summary>
    /// Stands an item that was placed higher than its legs reach on a support frame.
    /// Visual only, like the legs themselves.
    /// </summary>
    private static void AddSupports(SceneItem item, CatalogAsset asset, Node3D node)
    {
        foreach (SupportBeam beam in SceneSupports.For(item, asset))
        {
            Node3D model = Models.Instantiate("support_beam");
            model.AddToGroup("supports");
            model.Position = new Vector3((float)beam.X, (float)beam.Y, (float)beam.Z);
            // The model stands along +Y; lay it down for the beams of the ring.
            model.RotationDegrees = beam.Axis switch
            {
                'X' => new Vector3(0.0f, 0.0f, -90.0f),
                'Z' => new Vector3(90.0f, 0.0f, 0.0f),
                _ => Vector3.Zero,
            };
            model.Scale = new Vector3(1.0f, (float)beam.Length, 1.0f);
            node.AddChild(model);
        }
    }

    /// <summary>
    /// A belt with a forward and a reverse tag (plan 12). Both writes of a tick land in two fields; the drive
    /// is worked out again after each, so after the second one it holds for this tick.
    /// </summary>
    private void AddTwoWayDrive(string name, Conveyor conveyor)
    {
        bool forward = false;
        bool reverse = false;
        void Apply()
        {
            (ConveyorDirection direction, bool conflict) = ConveyorDrive.Direction(forward, reverse);
            conveyor.Backward = direction == ConveyorDirection.Backward;
            conveyor.Running = direction != ConveyorDirection.Stopped;
            conveyor.Conflict = conflict;
        }

        Actuators.Add((name, value =>
        {
            forward = value;
            Apply();
        }));
        Actuators.Add((SceneTags.ConveyorReverse(name), value =>
        {
            reverse = value;
            Apply();
        }));
        DrivenTags.Add(name);
        DrivenTags.Add(SceneTags.ConveyorReverse(name));
        Conflicts.Add((name, () => conveyor.Conflict, $"{name}: vooruit en achteruit tegelijk"));
        _resets.Add(() =>
        {
            forward = false;
            reverse = false;
            Apply();
        });
    }

    private void AfterAdded(SceneItem item, CatalogAsset asset, Node3D node)
    {
        switch (node)
        {
            case Conveyor conveyor:
                if (item.Param(SceneTags.AlwaysOn, asset, 0.0) > 0.5)
                {
                    conveyor.Running = true;
                }
                else
                {
                    if (item.Param(SceneTags.TwoWay, asset, 0.0) > 0.5)
                    {
                        AddTwoWayDrive(item.Name, conveyor);
                    }
                    else
                    {
                        Actuators.Add((item.Name, value => conveyor.Running = value));
                        DrivenTags.Add(item.Name);
                        _resets.Add(() => conveyor.Running = false);
                    }
                }

                if (conveyor.Weighs)
                {
                    AnalogSensors.Add((SceneTags.ConveyorWeight(item.Name), () => conveyor.WeightKg));
                }

                break;

            case CurveConveyor curve:
                if (item.Param(SceneTags.AlwaysOn, asset, 0.0) > 0.5)
                {
                    curve.Running = true;
                }
                else
                {
                    Actuators.Add((item.Name, value => curve.Running = value));
                    DrivenTags.Add(item.Name);
                    _resets.Add(() => curve.Running = false);
                }

                break;

            case StopGate stop:
                Actuators.Add((item.Name, value => stop.Raised = value));
                _resets.Add(stop.Reset);
                break;

            case Pusher pusher:
                if (pusher.Bistable)
                {
                    Actuators.Add((item.Name, value => pusher.ExtendCoil = value));
                    Actuators.Add((SceneTags.PusherRetract(item.Name), value => pusher.RetractCoil = value));
                    Conflicts.Add((item.Name, () => pusher.Conflict, $"{item.Name}: uit en in tegelijk"));
                }
                else
                {
                    Actuators.Add((item.Name, value => pusher.Extend = value));
                }

                Sensors.Add((SceneTags.PusherBack(item.Name), () => pusher.AtBack));
                Sensors.Add((SceneTags.PusherFront(item.Name), () => pusher.AtFront));
                _resets.Add(pusher.Reset);
                break;

            case PhotoEye eye:
                if (asset.Behaviour == AssetBehaviours.Vision)
                {
                    AnalogSensors.Add((item.Name, () => ItemKinds.Number(eye.SeenItem)));
                    Sensors.Add((SceneTags.VisionDetected(item.Name), () => eye.Detecting));
                    break;
                }

                Sensors.Add((item.Name, () => eye.Detecting));
                if (asset.Behaviour == AssetBehaviours.LightArray)
                {
                    AnalogSensors.Add((SceneTags.LightArrayCount(item.Name), () => eye.InterruptedBeams));
                }

                break;

            case Encoder encoder:
                Sensors.Add((SceneTags.EncoderA(item.Name), () => encoder.A));
                Sensors.Add((SceneTags.EncoderB(item.Name), () => encoder.B));
                _resets.Add(encoder.Reset);
                break;

            case PositioningBars bars:
                Actuators.Add((item.Name, value => bars.Close = value));
                Sensors.Add((SceneTags.ClampOpen(item.Name), () => bars.IsOpen));
                Sensors.Add((SceneTags.ClampClosed(item.Name), () => bars.IsClosed));
                _resets.Add(bars.Reset);
                break;

            case Emitter emitter:
                Actuators.Add((item.Name, value => emitter.Enabled = value));
                Emitters.Add(emitter);
                emitter.Spawned += box => ItemEmitted?.Invoke(item.Name, box);
                _resets.Add(emitter.Reset);
                break;

            case Remover remover:
                Removers.Add((item.Name, remover));
                remover.ItemRemoved += box => ItemRemoved?.Invoke(item.Name, box);
                _resets.Add(remover.Reset);
                break;

            case WheelSorter wheels:
                Actuators.Add((item.Name, value => wheels.Run = value));
                Actuators.Add((SceneTags.SorterLeft(item.Name), value => wheels.Left = value));
                Actuators.Add((SceneTags.SorterRight(item.Name), value => wheels.Right = value));
                DrivenTags.Add(item.Name);
                _resets.Add(wheels.Reset);
                break;

            case Turntable table:
                Actuators.Add((item.Name, value => table.Forward = value));
                Actuators.Add((SceneTags.TurntableReverse(item.Name), value => table.Reverse = value));
                Actuators.Add((SceneTags.TurntableTurn(item.Name), value => table.Turn = value));
                DrivenTags.AddRange(new[] { item.Name, SceneTags.TurntableReverse(item.Name) });
                Sensors.Add((SceneTags.TurntableAt0(item.Name), () => table.AtHome));
                Sensors.Add((SceneTags.TurntableAt90(item.Name), () => table.AtTurned));
                _resets.Add(table.Reset);
                break;

            case Palletizer station:
                Actuators.Add((SceneTags.PalletizerFeed(item.Name), value => station.Feed = value));
                Actuators.Add((SceneTags.PalletizerPush(item.Name), value => station.Push = value));
                Actuators.Add((SceneTags.PalletizerOpen(item.Name), value => station.Open = value));
                Actuators.Add((SceneTags.PalletizerUp(item.Name), value => station.Up = value));
                Actuators.Add((SceneTags.PalletizerDown(item.Name), value => station.Down = value));
                Actuators.Add((SceneTags.PalletizerRollers(item.Name), value => station.Rollers = value));
                Sensors.Add((SceneTags.PalletizerRowFull(item.Name), () => station.RowFull));
                Sensors.Add((SceneTags.PalletizerPusherBack(item.Name), () => station.PusherBack));
                Sensors.Add((SceneTags.PalletizerPusherFront(item.Name), () => station.PusherFront));
                Sensors.Add((SceneTags.PalletizerPlateOpen(item.Name), () => station.PlateOpen));
                Sensors.Add((SceneTags.PalletizerPlateClosed(item.Name), () => station.PlateClosed));
                Sensors.Add((SceneTags.PalletizerStackHigh(item.Name), () => station.StackHigh));
                Sensors.Add((SceneTags.PalletizerTableDown(item.Name), () => station.TableDown));
                Sensors.Add((SceneTags.PalletizerPallet(item.Name), () => station.PalletPresent));
                _resets.Add(station.Reset);
                break;

            case MachiningCenter machine:
                Actuators.Add((SceneTags.MachiningStart(item.Name), value => machine.Start = value));
                Actuators.Add((SceneTags.MachiningLid(item.Name), value => machine.Lid = value));
                Sensors.Add((SceneTags.MachiningBusy(item.Name), () => machine.Busy));
                Sensors.Add((SceneTags.MachiningEntry(item.Name), () => machine.EntryPresent));
                Sensors.Add((SceneTags.MachiningExit(item.Name), () => machine.ExitOccupied));
                _resets.Add(machine.Reset);
                break;

            case StackerStation crane:
            {
                FactorySim.Core.StackerCrane logic = crane.Logic;
                Actuators.Add((SceneTags.StackerForksLeft(item.Name), value => logic.ForksLeft = value));
                Actuators.Add((SceneTags.StackerForksRight(item.Name), value => logic.ForksRight = value));
                Actuators.Add((SceneTags.StackerLift(item.Name), value => logic.Lift = value));
                Sensors.Add((SceneTags.StackerForksMiddle(item.Name), () => logic.ForksMiddle));
                Sensors.Add((SceneTags.StackerForksLeftEnd(item.Name), () => logic.ForksLeftEnd));
                Sensors.Add((SceneTags.StackerForksRightEnd(item.Name), () => logic.ForksRightEnd));
                Sensors.Add((SceneTags.StackerLifted(item.Name), () => logic.Lifted));
                Sensors.Add((SceneTags.StackerLoad(item.Name), () => crane.Load));
                if (logic.Jog)
                {
                    Actuators.Add((SceneTags.StackerJogDirection(item.Name, "left"), value => logic.Left = value));
                    Actuators.Add((SceneTags.StackerJogDirection(item.Name, "right"), value => logic.Right = value));
                    Actuators.Add((SceneTags.StackerJogDirection(item.Name, "up"), value => logic.Up = value));
                    Actuators.Add((SceneTags.StackerJogDirection(item.Name, "down"), value => logic.Down = value));
                    for (int column = 1; column <= StackerPositions.Columns; column++)
                    {
                        int number = column;
                        Sensors.Add((SceneTags.StackerAtColumn(item.Name, number), () => logic.AtColumn(number)));
                    }

                    for (int level = 0; level <= StackerPositions.Levels; level++)
                    {
                        int number = level;
                        Sensors.Add((SceneTags.StackerAtLevel(item.Name, number), () => logic.AtLevel(number)));
                    }
                }
                else
                {
                    Actuators.Add((SceneTags.StackerTarget(item.Name, 1), value => logic.Target1 = value));
                    Actuators.Add((SceneTags.StackerTarget(item.Name, 2), value => logic.Target2 = value));
                    Actuators.Add((SceneTags.StackerTarget(item.Name, 4), value => logic.Target4 = value));
                    Actuators.Add((SceneTags.StackerTarget(item.Name, 8), value => logic.Target8 = value));
                    Actuators.Add((SceneTags.StackerGo(item.Name), value => logic.Go = value));
                    Sensors.Add((SceneTags.StackerBusy(item.Name), () => logic.Busy));
                    Sensors.Add((SceneTags.StackerAtTarget(item.Name), () => logic.AtTarget));
                    Sensors.Add((SceneTags.StackerError(item.Name), () => logic.Error));
                }

                _resets.Add(crane.Reset);
                break;
            }

            case Elevator lift:
                Actuators.Add((item.Name, value => lift.Forward = value));
                Actuators.Add((SceneTags.TurntableReverse(item.Name), value => lift.Reverse = value));
                Actuators.Add((SceneTags.ElevatorUp(item.Name), value => lift.Up = value));
                DrivenTags.AddRange(new[] { item.Name, SceneTags.TurntableReverse(item.Name) });
                Sensors.Add((SceneTags.ElevatorAtBottom(item.Name), () => lift.AtBottom));
                Sensors.Add((SceneTags.ElevatorAtTop(item.Name), () => lift.AtTop));
                _resets.Add(lift.Reset);
                break;

            case RobotArm robot:
            {
                RobotController controller = robot.Controller;
                controller.SetPoses(RobotPoses.ToTable(item.Poses));
                int[] weights = { 1, 2, 4, 8 };
                Action<bool>[] bits =
                {
                    value => controller.Pos1 = value, value => controller.Pos2 = value,
                    value => controller.Pos4 = value, value => controller.Pos8 = value,
                };
                for (int i = 0; i < weights.Length; i++)
                {
                    Actuators.Add((SceneTags.RobotPosition(item.Name, weights[i]), bits[i]));
                }

                Actuators.Add((SceneTags.RobotGo(item.Name), value => controller.Go = value));
                Actuators.Add((SceneTags.RobotGrip(item.Name), value => controller.Grip = value));
                Sensors.Add((SceneTags.RobotBusy(item.Name), () => controller.Busy));
                Sensors.Add((SceneTags.RobotAtPosition(item.Name), () => controller.AtPosition));
                Sensors.Add((SceneTags.RobotItem(item.Name), () => robot.ItemDetected));
                Sensors.Add((SceneTags.RobotError(item.Name), () => controller.Error));
                _resets.Add(robot.Reset);
                break;
            }

            case PickAndPlace station:
                Actuators.Add((SceneTags.PickPlaceAxis(item.Name, "X"), value => station.MoveX = value));
                Sensors.Add((SceneTags.PickPlaceMoving(item.Name, "X"), () => station.MovingX));
                if (station.HasY)
                {
                    Actuators.Add((SceneTags.PickPlaceAxis(item.Name, "Y"), value => station.MoveY = value));
                    Sensors.Add((SceneTags.PickPlaceMoving(item.Name, "Y"), () => station.MovingY));
                }

                Actuators.Add((SceneTags.PickPlaceAxis(item.Name, "Z"), value => station.MoveZ = value));
                Sensors.Add((SceneTags.PickPlaceMoving(item.Name, "Z"), () => station.MovingZ));
                Actuators.Add((SceneTags.PickPlaceGrab(item.Name), value => station.Grab = value));
                Sensors.Add((SceneTags.PickPlaceItem(item.Name), () => station.ItemDetected));
                _resets.Add(station.Reset);
                break;

            case ArmSorter arm:
                Actuators.Add((item.Name, value => arm.Out = value));
                _resets.Add(arm.Reset);
                break;

            case OperatorControl control:
                Sensors.Add((item.Name, () => control.Signal));
                Controls.Add(control);
                _resets.Add(control.Reset);
                break;

            case Potentiometer pot:
                AnalogSensors.Add((item.Name, () => pot.Value));
                Potentiometers.Add(pot);
                _resets.Add(pot.Reset);
                break;

            case DigitalDisplay display:
                AnalogActuators.Add((item.Name, display.SetNumber));
                _resets.Add(display.Reset);
                break;

            case Siren siren:
                Actuators.Add((item.Name, siren.Set));
                _resets.Add(siren.Reset);
                break;

            case Tank tank:
                AnalogActuators.Add((SceneTags.TankFillValve(item.Name), value => tank.FillValve = value));
                AnalogActuators.Add((SceneTags.TankDischargeValve(item.Name), value => tank.DischargeValve = value));
                AnalogSensors.Add((SceneTags.TankLevel(item.Name), () => tank.Level));
                AnalogSensors.Add((SceneTags.TankFlow(item.Name), () => tank.FlowLitres));
                _resets.Add(tank.Reset);
                break;

            case IndicatorLamp lamp:
            {
                IReadOnlyList<(string Part, string Suffix)> parts = SceneTags.LampParts(asset);
                for (int i = 0; i < parts.Count; i++)
                {
                    int index = i;
                    Actuators.Add((item.Name + parts[i].Suffix, value => lamp.Set(index, value)));
                }

                _resets.Add(lamp.Reset);
                break;
            }
        }
    }

    /// <summary>
    /// Stands a control or lamp on a post from the floor, unless the item says "post": 0
    /// (for example when it is mounted on a column or a cabinet). Assets with "onTop" sit
    /// on top of the post; the others are mounted against its side.
    /// </summary>
    private static void AddPost(Node3D part, SceneItem item, CatalogAsset asset)
    {
        if (item.Param("post", asset, 1.0) < 0.5)
        {
            return;
        }

        const float postRadius = 0.07f;
        bool onTop = item.Param("onTop", asset, 0.0) > 0.5;
        Node3D post = Models.Instantiate("sensor_post");
        post.Name = "Post";
        post.Position = new Vector3(0.0f, -(float)item.Y, onTop ? 0.0f : (float)asset.BoundsMin[2] - postRadius);

        // The post model is 1.26 m tall (sensor_post); one that hangs higher (the siren, at 2 m) gets a longer post.
        const float postHeight = 1.26f;
        if (item.Y > postHeight)
        {
            post.Scale = new Vector3(1.0f, (float)item.Y / postHeight, 1.0f);
        }

        part.AddChild(post);
    }

    private static Node3D BuildRail(SceneItem item, CatalogAsset asset, Vector3 position, Vector3 rotation)
    {
        float length = (float)item.Param("length", asset, 1.0);
        StaticBody3D rail = Shapes.StaticCollider("Rail", new Vector3(length, 0.08f, 0.04f), position, friction: 0.2f);
        rail.RotationDegrees = rotation;

        Node3D model = Models.Instantiate(asset.Id);
        model.Scale = new Vector3(length, 1.0f, 1.0f);       // modelled 1 m long
        rail.AddChild(model);

        // Brackets carry the rail from the conveyor frame; bracketSide says which side that is.
        float bracketYaw = item.Param("bracketSide", asset, 1.0) > 0.0 ? 0.0f : 180.0f;
        int brackets = Mathf.Max(2, Mathf.CeilToInt(length / 0.9f) + 1);
        float inset = Mathf.Min(0.12f, length / 4.0f);
        for (int i = 0; i < brackets; i++)
        {
            Node3D bracket = Models.Instantiate("rail_bracket");
            bracket.Name = $"Bracket{i}";
            bracket.Position = new Vector3(-length / 2.0f + inset + (length - 2.0f * inset) * i / (brackets - 1), 0.0f, 0.0f);
            bracket.RotationDegrees = new Vector3(0.0f, bracketYaw, 0.0f);
            rail.AddChild(bracket);
        }

        return rail;
    }

    private static string SafeNodeName(string id) =>
        new(id.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
}
