using System.Globalization;
using System.Text;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Scenes;

/// <summary>
/// Derives the tags of a scene from its items. Every functional item contributes its
/// sensors and actuators; the operator panel adds four fixed tags.
/// </summary>
public static class SceneTags
{
    public const string StartButton = "Start button";
    public const string StopButton = "Stop button";
    public const string SimRunning = "Sim running";
    public const string RunLamp = "Run lamp";

    /// <summary>Optional (plan 14): high while the simulation is paused, not when it is stopped.</summary>
    public const string SimPaused = "Sim paused";

    /// <summary>Optional (plan 14): high for one second of real time after each reset.</summary>
    public const string SimReset = "Sim reset";

    /// <summary>The optional simulation tags a scene can switch on, in the order they get their addresses.</summary>
    public static readonly IReadOnlyList<string> OptionalSimTags = new[] { SimPaused, SimReset };

    /// <summary>Parameter that makes a conveyor run without a tag (value 1).</summary>
    public const string AlwaysOn = "alwaysOn";

    /// <summary>Parameter that makes a belt that climbs run downhill instead (value 1). A setting, not a tag.</summary>
    public const string Reverse = "reverse";

    /// <summary>Parameter that gives a conveyor a second tag for running backwards (value 1); plan 12.</summary>
    public const string TwoWay = "twoWay";

    /// <summary>Parameter that gives a pusher a bistable valve with a coil for out and one for in (value 1); plan 12.</summary>
    public const string Bistable = "bistable";

    /// <summary>Conveyor with <see cref="TwoWay"/>: run backwards.</summary>
    public static string ConveyorReverse(string name) => name + " reverse";

    /// <summary>Pusher with <see cref="Bistable"/>: the coil that pulls it in.</summary>
    public static string PusherRetract(string name) => name + " retract";

    public static string PusherBack(string name) => name + " back";

    public static string PusherFront(string name) => name + " front";

    /// <summary>Track A of an encoder.</summary>
    public static string EncoderA(string name) => name + " A";

    /// <summary>Track B of an encoder, a quarter period behind A when moving forward.</summary>
    public static string EncoderB(string name) => name + " B";

    /// <summary>Limit switch: the clamps of the positioning bars are fully open.</summary>
    public static string ClampOpen(string name) => name + " open";

    /// <summary>Sensor: the clamps have stopped closing, on an item or at the end of their stroke.</summary>
    public static string ClampClosed(string name) => name + " closed";

    /// <summary>
    /// The lamps of an asset and the suffix each adds to the item name for its tag.
    /// A model part called "Lamp" is the only lamp and gets no suffix; parts called
    /// "Lamp_Green", "Lamp_Red", ... each get their own tag ("Stack light green").
    /// </summary>
    public static IReadOnlyList<(string Part, string Suffix)> LampParts(CatalogAsset asset)
    {
        var lamps = new List<(string, string)>();
        foreach (string part in asset.Parts)
        {
            if (part == "Lamp")
            {
                lamps.Add((part, string.Empty));
            }
            else if (part.StartsWith("Lamp_", StringComparison.Ordinal))
            {
                lamps.Add((part, " " + part["Lamp_".Length..].ToLowerInvariant()));
            }
        }

        return lamps;
    }

    /// <summary>Tag that steers a wheel sorter to the left, seen in the direction of travel.</summary>
    public static string SorterLeft(string itemName) => itemName + " left";

    /// <summary>Tag that steers a wheel sorter to the right, seen in the direction of travel.</summary>
    public static string SorterRight(string itemName) => itemName + " right";

    /// <summary>Tag that sends an elevator up; off brings it down.</summary>
    public static string ElevatorUp(string itemName) => itemName + " up";

    /// <summary>Limit switch: the elevator is at its lower level.</summary>
    public static string ElevatorAtBottom(string itemName) => itemName + " at bottom";

    /// <summary>Limit switch: the elevator is at its upper level.</summary>
    public static string ElevatorAtTop(string itemName) => itemName + " at top";

    /// <summary>Palletizer: runs the infeed lane on the layer plate.</summary>
    public static string PalletizerFeed(string itemName) => itemName + " feed";

    /// <summary>Palletizer: pushes the row from the infeed lane onto the plate; off retracts.</summary>
    public static string PalletizerPush(string itemName) => itemName + " push";

    /// <summary>Palletizer: opens the layer plate so the layer drops; off closes it.</summary>
    public static string PalletizerOpen(string itemName) => itemName + " open";

    /// <summary>Palletizer: lifting table up while on.</summary>
    public static string PalletizerUp(string itemName) => itemName + " up";

    /// <summary>Palletizer: lifting table down while on.</summary>
    public static string PalletizerDown(string itemName) => itemName + " down";

    /// <summary>Palletizer: rollers of the lifting table.</summary>
    public static string PalletizerRollers(string itemName) => itemName + " rollers";

    /// <summary>Palletizer sensor: a box is at the last place of the row.</summary>
    public static string PalletizerRowFull(string itemName) => itemName + " row full";

    public static string PalletizerPusherBack(string itemName) => itemName + " pusher back";

    public static string PalletizerPusherFront(string itemName) => itemName + " pusher front";

    public static string PalletizerPlateOpen(string itemName) => itemName + " plate open";

    public static string PalletizerPlateClosed(string itemName) => itemName + " plate closed";

    /// <summary>Palletizer sensor: the pallet or the stack on it is just under the plate.</summary>
    public static string PalletizerStackHigh(string itemName) => itemName + " stack high";

    public static string PalletizerTableDown(string itemName) => itemName + " table down";

    /// <summary>Palletizer sensor: a pallet is in place on the lifting table.</summary>
    public static string PalletizerPallet(string itemName) => itemName + " pallet";

    /// <summary>Machining center: starts a cycle when a part is at the entry.</summary>
    public static string MachiningStart(string itemName) => itemName + " start";

    /// <summary>Machining center: on makes a lid of the next part, off a base.</summary>
    public static string MachiningLid(string itemName) => itemName + " lid";

    /// <summary>Machining center sensor: a cycle is running.</summary>
    public static string MachiningBusy(string itemName) => itemName + " busy";

    /// <summary>Machining center sensor: a part lies at the pick position.</summary>
    public static string MachiningEntry(string itemName) => itemName + " entry";

    /// <summary>Machining center sensor: a part lies on the exit deck.</summary>
    public static string MachiningExit(string itemName) => itemName + " exit";

    /// <summary>Parameter: 1 lets the PLC jog a stacker crane instead of naming a target slot.</summary>
    public const string StackerJog = "jog";

    public static string StackerForksLeft(string itemName) => itemName + " forks left";

    public static string StackerForksRight(string itemName) => itemName + " forks right";

    /// <summary>Stacker crane: raises the forks a little, lifting a pallet off its support.</summary>
    public static string StackerLift(string itemName) => itemName + " lift";

    /// <summary>Stacker crane, target mode: bit of the slot number; <paramref name="weight"/> is 1, 2, 4 or 8.</summary>
    public static string StackerTarget(string itemName, int weight) => itemName + " target " + weight;

    /// <summary>Stacker crane, target mode: a rising flank sends the crane to the slot.</summary>
    public static string StackerGo(string itemName) => itemName + " go";

    /// <summary>Stacker crane, jog mode: <paramref name="direction"/> is "left", "right", "up" or "down".</summary>
    public static string StackerJogDirection(string itemName, string direction) => itemName + " " + direction;

    public static string StackerForksMiddle(string itemName) => itemName + " forks middle";

    public static string StackerForksLeftEnd(string itemName) => itemName + " forks left end";

    public static string StackerForksRightEnd(string itemName) => itemName + " forks right end";

    public static string StackerLifted(string itemName) => itemName + " lifted";

    /// <summary>Stacker crane sensor: something lies on the forks.</summary>
    public static string StackerLoad(string itemName) => itemName + " load";

    public static string StackerBusy(string itemName) => itemName + " busy";

    public static string StackerAtTarget(string itemName) => itemName + " at target";

    public static string StackerError(string itemName) => itemName + " error";

    public static string StackerAtColumn(string itemName, int column) => itemName + " at column " + column;

    public static string StackerAtLevel(string itemName, int level) => itemName + " at level " + level;

    /// <summary>Tag that runs the rollers of a turntable or an elevator backwards.</summary>
    public static string TurntableReverse(string itemName) => itemName + " reverse";

    /// <summary>Tag that turns a turntable a quarter turn; off turns it back.</summary>
    public static string TurntableTurn(string itemName) => itemName + " turn";

    /// <summary>Limit switch: the turntable is in its home position.</summary>
    public static string TurntableAt0(string itemName) => itemName + " at 0";

    /// <summary>Limit switch: the turntable has made its quarter turn.</summary>
    public static string TurntableAt90(string itemName) => itemName + " at 90";

    /// <summary>Parameter: stroke of the Y axis of a pick and place station; 0 means it has none.</summary>
    public const string StrokeY = "strokeY";

    /// <summary>Tag that sends an axis ("X", "Y" or "Z") of a pick and place station to the end of its stroke.</summary>
    public static string PickPlaceAxis(string itemName, string axis) => itemName + " " + axis;

    /// <summary>Tag that closes the gripper of a pick and place station.</summary>
    public static string PickPlaceGrab(string itemName) => itemName + " grab";

    /// <summary>Sensor: an axis of a pick and place station is moving.</summary>
    public static string PickPlaceMoving(string itemName, string axis) => itemName + " moving " + axis;

    /// <summary>Sensor: the gripper of a pick and place station touches or holds an item.</summary>
    public static string PickPlaceItem(string itemName) => itemName + " item";

    /// <summary>Robot arm: bit of the position number; <paramref name="weight"/> is 1, 2, 4 or 8.</summary>
    public static string RobotPosition(string itemName, int weight) => itemName + " pos " + weight;

    /// <summary>Robot arm: a rising flank sends the arm to the position the bits name.</summary>
    public static string RobotGo(string itemName) => itemName + " go";

    /// <summary>Robot arm: the suction cup holds while this is on.</summary>
    public static string RobotGrip(string itemName) => itemName + " grip";

    public static string RobotBusy(string itemName) => itemName + " busy";

    /// <summary>Robot arm: standing still on the position the bits name.</summary>
    public static string RobotAtPosition(string itemName) => itemName + " at pos";

    /// <summary>Robot arm: the suction cup touches or holds an item.</summary>
    public static string RobotItem(string itemName) => itemName + " item";

    /// <summary>Robot arm: the last go named a position that was not taught.</summary>
    public static string RobotError(string itemName) => itemName + " error";

    /// <summary>Parameter that gives a conveyor a weight tag (value 1): the weighing conveyor.</summary>
    public const string Weighs = "weighs";

    /// <summary>Weighing conveyor: total mass of the items resting on the belt, in kg × 100.</summary>
    public static string ConveyorWeight(string itemName) => itemName + " weight";

    /// <summary>Light array: number of interrupted beams.</summary>
    public static string LightArrayCount(string itemName) => itemName + " count";

    /// <summary>Tank: position of the fill valve, 0-100 %.</summary>
    public static string TankFillValve(string itemName) => itemName + " fill valve";

    /// <summary>Tank: position of the discharge valve, 0-100 %.</summary>
    public static string TankDischargeValve(string itemName) => itemName + " discharge valve";

    /// <summary>Tank: height of the liquid, in m × 100 (cm).</summary>
    public static string TankLevel(string itemName) => itemName + " level";

    /// <summary>Tank: discharge flow, in l/s × 10.</summary>
    public static string TankFlow(string itemName) => itemName + " flow";

    /// <summary>Vision sensor: an item is in view.</summary>
    public static string VisionDetected(string itemName) => itemName + " detected";

    /// <summary>Scale of the weight tag: 0,01 kg per step.</summary>
    public const double WeightScale = 100;

    /// <summary>Scale of the level tag: 0,01 m per step.</summary>
    public const double LevelScale = 100;

    /// <summary>Scale of the flow tag: 0,1 l/s per step.</summary>
    public const double FlowScale = 10;

    /// <summary>Names and directions of the tags an item needs, in a fixed order.</summary>
    public static IEnumerable<TagDefinition> TagsOf(SceneItem item, CatalogAsset asset)
    {
        switch (asset.Behaviour)
        {
            case AssetBehaviours.Conveyor when item.Param(AlwaysOn, asset, 0.0) < 0.5:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                if (item.Param(TwoWay, asset, 0.0) > 0.5)
                {
                    yield return new TagDefinition(ConveyorReverse(item.Name), TagDirection.Actuator);
                }

                break;
            case AssetBehaviours.Curve when item.Param(AlwaysOn, asset, 0.0) < 0.5:
            case AssetBehaviours.Emitter:
            case AssetBehaviours.Stop:
            case AssetBehaviours.ArmSorter:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                break;
            case AssetBehaviours.Pusher:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                if (item.Param(Bistable, asset, 0.0) > 0.5)
                {
                    yield return new TagDefinition(PusherRetract(item.Name), TagDirection.Actuator);
                }

                yield return new TagDefinition(PusherBack(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PusherFront(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.PhotoEye:
            case AssetBehaviours.Retroreflective:
            case AssetBehaviours.LightArray:
            case AssetBehaviours.Button:
                yield return new TagDefinition(item.Name, TagDirection.Sensor);
                break;
            case AssetBehaviours.Potentiometer:
                yield return new TagDefinition(item.Name, TagDirection.Sensor, TagKind.Int, Min: 0, Max: KnobLogic.Maximum);
                break;
            case AssetBehaviours.Display:
                yield return new TagDefinition(item.Name, TagDirection.Actuator, TagKind.Int);
                break;
            case AssetBehaviours.Siren:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                break;
            case AssetBehaviours.Vision:
                yield return new TagDefinition(item.Name, TagDirection.Sensor, TagKind.Int, Min: 0, Max: ItemKinds.NumberedItems.Count);
                yield return new TagDefinition(VisionDetected(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.Tank:
            {
                int top = (int)Math.Round(asset.Param("height", 3.0) * LevelScale);
                yield return new TagDefinition(TankFillValve(item.Name), TagDirection.Actuator, TagKind.Int, Unit: "%", Min: 0, Max: 100);
                yield return new TagDefinition(TankDischargeValve(item.Name), TagDirection.Actuator, TagKind.Int, Unit: "%", Min: 0, Max: 100);
                yield return new TagDefinition(TankLevel(item.Name), TagDirection.Sensor, TagKind.Int, LevelScale, "m", 0, top);
                yield return new TagDefinition(TankFlow(item.Name), TagDirection.Sensor, TagKind.Int, FlowScale, "l/s", 0, short.MaxValue);
                break;
            }

            case AssetBehaviours.Encoder:
                yield return new TagDefinition(EncoderA(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(EncoderB(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.Clamp:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                yield return new TagDefinition(ClampOpen(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(ClampClosed(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.WheelSorter:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                yield return new TagDefinition(SorterLeft(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(SorterRight(item.Name), TagDirection.Actuator);
                break;
            case AssetBehaviours.Turntable:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                yield return new TagDefinition(TurntableReverse(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(TurntableTurn(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(TurntableAt0(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(TurntableAt90(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.Palletizer:
                yield return new TagDefinition(PalletizerFeed(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerPush(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerOpen(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerUp(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerDown(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerRollers(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(PalletizerRowFull(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerPusherBack(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerPusherFront(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerPlateOpen(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerPlateClosed(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerStackHigh(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerTableDown(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(PalletizerPallet(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.Machining:
                yield return new TagDefinition(MachiningStart(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(MachiningLid(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(MachiningBusy(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(MachiningEntry(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(MachiningExit(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.StackerCrane:
            {
                yield return new TagDefinition(StackerForksLeft(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(StackerForksRight(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(StackerLift(item.Name), TagDirection.Actuator);
                if (item.Param(StackerJog, asset, 0.0) >= 0.5)
                {
                    foreach (string direction in new[] { "left", "right", "up", "down" })
                    {
                        yield return new TagDefinition(StackerJogDirection(item.Name, direction), TagDirection.Actuator);
                    }
                }
                else
                {
                    foreach (int weight in new[] { 1, 2, 4, 8 })
                    {
                        yield return new TagDefinition(StackerTarget(item.Name, weight), TagDirection.Actuator);
                    }

                    yield return new TagDefinition(StackerGo(item.Name), TagDirection.Actuator);
                }

                yield return new TagDefinition(StackerForksMiddle(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(StackerForksLeftEnd(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(StackerForksRightEnd(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(StackerLifted(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(StackerLoad(item.Name), TagDirection.Sensor);
                if (item.Param(StackerJog, asset, 0.0) >= 0.5)
                {
                    for (int column = 1; column <= StackerPositions.Columns; column++)
                    {
                        yield return new TagDefinition(StackerAtColumn(item.Name, column), TagDirection.Sensor);
                    }

                    for (int level = 0; level <= StackerPositions.Levels; level++)
                    {
                        yield return new TagDefinition(StackerAtLevel(item.Name, level), TagDirection.Sensor);
                    }
                }
                else
                {
                    yield return new TagDefinition(StackerBusy(item.Name), TagDirection.Sensor);
                    yield return new TagDefinition(StackerAtTarget(item.Name), TagDirection.Sensor);
                    yield return new TagDefinition(StackerError(item.Name), TagDirection.Sensor);
                }

                break;
            }

            case AssetBehaviours.Elevator:
                yield return new TagDefinition(item.Name, TagDirection.Actuator);
                yield return new TagDefinition(TurntableReverse(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(ElevatorUp(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(ElevatorAtBottom(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(ElevatorAtTop(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.Robot:
                foreach (int weight in new[] { 1, 2, 4, 8 })
                {
                    yield return new TagDefinition(RobotPosition(item.Name, weight), TagDirection.Actuator);
                }

                yield return new TagDefinition(RobotGo(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(RobotGrip(item.Name), TagDirection.Actuator);
                yield return new TagDefinition(RobotBusy(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(RobotAtPosition(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(RobotItem(item.Name), TagDirection.Sensor);
                yield return new TagDefinition(RobotError(item.Name), TagDirection.Sensor);
                break;
            case AssetBehaviours.PickPlace:
            {
                // The Y axis exists only on stations that have a stroke for it.
                string[] axes = item.Param(StrokeY, asset, 0.0) > 0.0 ? new[] { "X", "Y", "Z" } : new[] { "X", "Z" };
                foreach (string axis in axes)
                {
                    yield return new TagDefinition(PickPlaceAxis(item.Name, axis), TagDirection.Actuator);
                }

                yield return new TagDefinition(PickPlaceGrab(item.Name), TagDirection.Actuator);
                foreach (string axis in axes)
                {
                    yield return new TagDefinition(PickPlaceMoving(item.Name, axis), TagDirection.Sensor);
                }

                yield return new TagDefinition(PickPlaceItem(item.Name), TagDirection.Sensor);
                break;
            }

            case AssetBehaviours.Lamp:
                foreach ((string _, string suffix) in LampParts(asset))
                {
                    yield return new TagDefinition(item.Name + suffix, TagDirection.Actuator);
                }

                break;
        }

        // Int tags that come on top of the tags above.
        if (asset.Behaviour == AssetBehaviours.Conveyor && item.Param(Weighs, asset, 0.0) >= 0.5)
        {
            yield return new TagDefinition(ConveyorWeight(item.Name), TagDirection.Sensor, TagKind.Int, WeightScale, "kg", 0, short.MaxValue);
        }

        if (asset.Behaviour == AssetBehaviours.LightArray)
        {
            yield return new TagDefinition(LightArrayCount(item.Name), TagDirection.Sensor, TagKind.Int,
                Min: 0, Max: (int)Math.Round(item.Param("beams", asset, 8.0)));
        }
    }

    /// <summary>
    /// The tag map for the scene as it is now. Tags that already had an address in
    /// <see cref="Scene.Tags"/> keep it, so a PLC project stays valid when items are added;
    /// new tags get the lowest free offset in their area.
    /// </summary>
    public static IReadOnlyList<TagMapEntry> Build(Scene scene, AssetCatalog catalog)
    {
        var wanted = new List<TagDefinition>
        {
            new(StartButton, TagDirection.Sensor),
            new(StopButton, TagDirection.Sensor),
        };
        var seen = new HashSet<string>(wanted.Select(tag => tag.Name), StringComparer.Ordinal) { SimRunning, RunLamp, SimPaused, SimReset };
        foreach (SceneItem item in scene.Items)
        {
            CatalogAsset? asset = catalog.Find(item.Asset);
            if (asset is null)
            {
                continue;
            }

            foreach (TagDefinition tag in TagsOf(item, asset))
            {
                if (!seen.Add(tag.Name))
                {
                    throw new SceneException($"Two items would both get the tag '{tag.Name}'. Rename one of them.");
                }

                wanted.Add(tag);
            }
        }

        wanted.Add(new TagDefinition(SimRunning, TagDirection.Sensor));
        wanted.Add(new TagDefinition(RunLamp, TagDirection.Actuator));
        foreach (string name in OptionalSimTags.Where(scene.SimTags.Contains))
        {
            wanted.Add(new TagDefinition(name, TagDirection.Sensor));
        }

        return Assign(wanted, scene.Tags);
    }

    /// <summary>
    /// Gives each wanted tag its address: the saved one when it is still in the right area and free,
    /// otherwise the lowest free offset in its area. Bool tags go in coils and discrete inputs, Int tags
    /// in holding and input registers. The kind, scale and range of an Int tag always come from
    /// <paramref name="wanted"/>, so they follow the part.
    /// </summary>
    public static IReadOnlyList<TagMapEntry> Assign(IEnumerable<TagDefinition> wanted, IEnumerable<TagMapEntry> savedTags)
    {
        Dictionary<string, TagMapEntry> saved = savedTags
            .GroupBy(tag => tag.Name).ToDictionary(group => group.Key, group => group.First());
        var used = Enum.GetValues<ModbusArea>().ToDictionary(area => area, _ => new HashSet<int>());

        // First pass: tags that keep their saved address.
        var result = new List<TagMapEntry>();
        var pending = new List<TagDefinition>();
        foreach (TagDefinition tag in wanted)
        {
            ModbusArea area = TagMap.AreaFor(tag.Direction, tag.Kind);
            if (saved.TryGetValue(tag.Name, out TagMapEntry? entry) && entry.Direction == tag.Direction
                && entry.Area == area && entry.Offset >= 0 && used[area].Add(entry.Offset))
            {
                result.Add(tag.Kind == TagKind.Int ? WithAnalogFields(entry, tag) : entry);
            }
            else
            {
                pending.Add(tag);
            }
        }

        // Second pass: new tags take the lowest free offset.
        foreach (TagDefinition tag in pending)
        {
            ModbusArea area = TagMap.AreaFor(tag.Direction, tag.Kind);
            int offset = 0;
            while (!used[area].Add(offset))
            {
                offset++;
            }

            var entry = new TagMapEntry(tag.Name, tag.Direction, area, offset, IecAddress(tag.Direction, tag.Kind, offset),
                VariableName(tag.Direction, tag.Name));
            result.Add(tag.Kind == TagKind.Int ? WithAnalogFields(entry, tag) : entry);
        }

        return result.OrderBy(tag => tag.Direction).ThenBy(tag => tag.Area).ThenBy(tag => tag.Offset).ToArray();
    }

    /// <summary>An entry with the kind, scale, unit and range of an Int tag; defaults are left out.</summary>
    private static TagMapEntry WithAnalogFields(TagMapEntry entry, TagDefinition tag) => entry with
    {
        Kind = TagKind.Int,
        Scale = tag.Scale == 1 ? null : tag.Scale,
        Unit = tag.Unit,
        Min = tag.Min == short.MinValue ? null : tag.Min,
        Max = tag.Max == short.MaxValue ? null : tag.Max,
    };

    /// <summary>Things that stop a scene from running, in plain words. Empty when it is fine.</summary>
    public static IReadOnlyList<string> Problems(Scene scene, AssetCatalog catalog)
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal) { StartButton, StopButton, SimRunning, RunLamp, SimPaused, SimReset };
        foreach (SceneItem item in scene.Items)
        {
            CatalogAsset? asset = catalog.Find(item.Asset);
            if (asset is null)
            {
                problems.Add($"'{item.Name}' uses asset '{item.Asset}', which is not in the library.");
                continue;
            }

            foreach (TagDefinition tag in TagsOf(item, asset))
            {
                if (!names.Add(tag.Name))
                {
                    problems.Add($"The tag '{tag.Name}' would exist twice. Rename '{item.Name}'.");
                }
            }

            if (asset.Behaviour == AssetBehaviours.Emitter && item.Emits is { } emitsId)
            {
                CatalogAsset? emitted = catalog.Find(emitsId);
                if (emitted is null || emitted.Behaviour != AssetBehaviours.Item)
                {
                    problems.Add($"'{item.Name}' is set to emit '{emitsId}', which is not an item in the library.");
                }
                else if (!EmitterItems.Fits(asset, emitted))
                {
                    problems.Add($"'{item.Name}' is set to emit '{emitsId}', which does not fit through its portal.");
                }
            }
        }

        return problems;
    }

    /// <summary>%IX0.2 for a bit, %IW2 for a word (Int tag).</summary>
    private static string IecAddress(TagDirection direction, TagKind kind, int offset)
    {
        if (kind == TagKind.Int)
        {
            return (direction == TagDirection.Sensor ? "%IW" : "%QW") + offset.ToString(CultureInfo.InvariantCulture);
        }

        string prefix = direction == TagDirection.Sensor ? "%IX" : "%QX";
        return prefix + (offset / 8).ToString(CultureInfo.InvariantCulture) + "." + (offset % 8).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"At pusher" as a sensor becomes IO.iAtPusher.</summary>
    private static string VariableName(TagDirection direction, string tagName)
    {
        var name = new StringBuilder(direction == TagDirection.Sensor ? "IO.i" : "IO.o");
        bool upper = true;
        foreach (char c in tagName)
        {
            if (char.IsLetterOrDigit(c) && c < 128)
            {
                name.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        return name.ToString();
    }
}
