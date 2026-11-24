using System;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "blocks" (plan 18): building blocks with the editor's own operations.
/// 1. The pusher station of the MVP scene is selected and saved as a block, into a temporary folder.
/// 2. That block is placed twice in an empty scene: every name and tag unique, no overlap between the copies, one
///    undo step per placing.
/// 3. The example block *Pusherstation* goes into an empty scene with an emitter and two removers, and 10 boxes are
///    sorted with the program of sort:20: high boxes into the side lane, low ones to the end, none falls.
/// </summary>
public partial class SelfTest
{
    private const int BlocksBoxes = 10;

    private static readonly string[] StationIds =
    {
        "main-belt", "side-lane", "rail-back-a", "rail-back-b", "rail-front-a", "rail-front-b",
        "lane-rail-a", "lane-rail-b", "pusher", "eye-low", "eye-high",
    };

    private int _blPhase;
    private bool _blPass = true;
    private string _blFolder = string.Empty;

    /// <summary>Physics side: the sorting program of sort:20 on the tags of the placed example block.</summary>
    private void RunBlocksPhysics()
    {
        if (_blPhase != 1)
        {
            return;
        }

        _elapsed += GetPhysicsProcessDeltaTime();
        _maxFallen = Math.Max(_maxFallen, Main.CountBoxes().Fallen);
        if (!RunSortFor(BlocksBoxes))
        {
            return;
        }

        int removed = EndRemover.Count + LaneRemover.Count;
        BlocksCheck($"sorted: {removed} of {BlocksBoxes} removed, end low={EndRemover.LowCount} high={EndRemover.HighCount}, "
            + $"lane low={LaneRemover.LowCount} high={LaneRemover.HighCount}, fallen {_maxFallen}, {_elapsed:0.0} s",
            removed == BlocksBoxes && EndRemover.HighCount == 0 && LaneRemover.LowCount == 0 && LaneRemover.HighCount > 0
            && _maxFallen == 0);
        WriteActuators(conveyor: false, pusher: false, emitter: false);
        _blPhase = 2;
    }

    /// <summary>Frame side: builds and checks, then reports after the sorting.</summary>
    private void RunBlocksFrame()
    {
        switch (_blPhase)
        {
            case 0:
                BuildBlocks();
                _blPhase = _blPass ? 1 : 2;
                break;
            case 2:
                SaveScreenshot(suffix: "_blocks");
                if (_blFolder.Length > 0)
                {
                    try
                    {
                        System.IO.Directory.Delete(_blFolder, recursive: true);
                    }
                    catch (System.IO.IOException)
                    {
                        // A temporary folder that stays behind does no harm.
                    }
                }

                _reported = true;
                GD.Print(_blPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_blPass ? 0 : 1);
                break;
        }
    }

    private void BuildBlocks()
    {
        _blFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"factorysim-blocks-{System.Environment.ProcessId}");
        System.IO.Directory.CreateDirectory(_blFolder);
        Editor.BlocksFolder = _blFolder.Replace('\\', '/');

        // 1. Save the pusher station of the MVP scene as a block.
        Editor.SetDesignMode(true);
        BlocksCheck("MVP scene opened", Editor.LoadFrom("res://scenes/mvp_sorting.fsscene.json") is null);
        Editor.Select("main-belt");
        bool offeredForOne = Editor.SaveBlockOffered;
        Editor.SelectMany(StationIds);
        BlocksCheck($"button offered for a group only (one: {offeredForOne}, eleven: {Editor.SaveBlockOffered})",
            !offeredForOne && Editor.SaveBlockOffered);
        string? path = Editor.SaveSelectionAsBlock("Test station", "Zelftest");
        SceneBlock? saved = path is null ? null : SceneEditor.ReadBlock(path);
        BlocksCheck($"saved as {path}: {saved?.Items.Count} items", saved is { Name: "Test station", Items.Count: 11 });

        Editor.SetPaletteFilter(SceneEditor.BlocksCategory, string.Empty);
        string[] listed = Editor.PaletteBlocks.Select(entry => (entry.Own ? "own:" : "") + entry.Block.Name).ToArray();
        BlocksCheck($"library: {string.Join(", ", listed)}",
            listed.SequenceEqual(new[] { "Invoer", "Pusherstation", "Sorteerpunt", "own:Test station" }));
        Editor.SetPaletteFilter(null, string.Empty);
        if (saved is null)
        {
            _blPass = false;
            return;
        }

        // 2. Place it twice in an empty scene.
        Editor.NewScene();
        var first = Editor.PlaceBlock(saved, 0.0f, -3.0f);
        var second = Editor.PlaceBlock(saved, 0.0f, 2.75f, 180.0f);
        int names = Main.Scene.Items.Select(item => item.Name).Distinct().Count();
        var problems = SceneTags.Problems(Main.Scene, Main.Catalog);
        BlocksCheck($"placed twice: {Main.Scene.Items.Count} items, {names} names, {Main.Tags.ActuatorNames.Count} actuators, "
            + $"problems: {problems.Count}",
            first is { Count: 11 } && second is { Count: 11 } && names == 22 && problems.Count == 0
            && Main.Tags.ActuatorNames.Contains("Pusher 2") && Main.Tags.ActuatorNames.Contains("Conveyor 2"));
        BlocksCheck($"no overlap ({Editor.OverlapMarkerCount}{(Editor.OverlapWarning.Length > 0 ? ": " + Editor.OverlapWarning : "")})",
            Editor.OverlapMarkerCount == 0 && Editor.OverlapWarning.Length == 0);
        Editor.Undo();
        int afterUndo = Main.Scene.Items.Count;
        Editor.Redo();
        BlocksCheck($"one undo step per placing ({afterUndo} after undo, {Main.Scene.Items.Count} after redo)",
            afterUndo == 11 && Main.Scene.Items.Count == 22);

        // 3. The example block with an emitter and two removers, then sort 10 boxes.
        Editor.NewScene();
        SceneEditor.BlockEntry? example = Editor.Blocks().FirstOrDefault(entry => !entry.Own && entry.Block.Name == "Pusherstation");
        var station = example is null ? null : Editor.PlaceBlock(example.Block, 0.5f, 0.25f);
        SceneItem? emitter = Editor.Place("emitter_gantry", -2.5f, 0.0f);
        bool seeded = SetParam("seed", 1.0);
        SceneItem? end = Editor.Place("remover_hood_1m0", 3.25f, 0.0f);
        bool endNamed = Editor.RenameSelected("End remover") is null;
        SceneItem? lane = Editor.Place("remover_hood_1m4", 1.0f, 2.5f, 270.0f);
        bool laneNamed = Editor.RenameSelected("Lane remover") is null;
        BlocksCheck("example placed with emitter and removers",
            station is { Count: 11 } && emitter is not null && seeded && end is not null && lane is not null && endNamed && laneNamed);
        SceneItem? pusher = Main.Scene.Items.FirstOrDefault(item => item.Asset == "pusher");
        BlocksCheck($"pusher where the MVP scene has it ({pusher?.X}, {pusher?.Z})", pusher is { X: 1.0, Z: -0.48 });
        Editor.SetDesignMode(false);
        BlocksCheck("tags of the station", new[] { "Conveyor", "Pusher", "Emitter" }.All(Main.Tags.ActuatorNames.Contains));

        Main.Camera.Frame(new Vector3(0.5f, 0.8f, 0.5f), 8.0f);
        _step = Step.Transport;
        _elapsed = 0.0;
        _maxFallen = 0;
        Main.Sim.Run();
    }

    private void BlocksCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST blocks {what}: {(ok ? "ok" : "FAILED")}");
        _blPass &= ok;
    }
}
