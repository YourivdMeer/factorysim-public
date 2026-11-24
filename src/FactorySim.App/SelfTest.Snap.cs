using System;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "snap" (plan 17): builds belt → curve → belt with the editor's own operations and lets them click
/// together. The curve is placed with the wrong yaw and dragged near its place (it turns 270° and lands exactly);
/// the second belt comes from the library near the out of the curve. Also: dragging without clicking (Alt) stays on
/// the grid, and a click is one undo step. Then 10 low boxes ride over the line: all reach the remover, none falls.
/// </summary>
public partial class SelfTest
{
    private const int SnapBoxes = 10;
    private const double SnapTimeout = 80.0;

    private int _snPhase;
    private bool _snPass = true;
    private int _snEmitted;
    private int _snRemoved;
    private string _snEmitter = string.Empty;
    private bool _snHooked;

    /// <summary>Physics side: everything on until ten boxes are out, then only the belts; stop when they are all in.</summary>
    private void RunSnapPhysics()
    {
        if (_snPhase != 1)
        {
            return;
        }

        double now = Main.Sim.SimTime;
        bool done = _snRemoved >= SnapBoxes || now > SnapTimeout;
        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames
            .Select(name => !done && (name != _snEmitter || _snEmitted < SnapBoxes)).ToArray());
        if (done)
        {
            (int inScene, int fallen) = Main.CountBoxes();
            SnapCheck($"ride: {_snEmitted} emitted, {_snRemoved} at the remover, {fallen} fallen, {inScene} left, {now:0.0} s",
                _snEmitted == SnapBoxes && _snRemoved == SnapBoxes && fallen == 0);
            _snPhase = 2;
        }
    }

    /// <summary>Frame side: builds and checks the line, then reports after the ride.</summary>
    private void RunSnapFrame()
    {
        switch (_snPhase)
        {
            case 0:
                BuildSnapLine();
                _snPhase = _snPass ? 1 : 2;
                break;
            case 2:
                SaveScreenshot(suffix: "_snap");
                _reported = true;
                GD.Print(_snPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_snPass ? 0 : 1);
                break;
        }
    }

    private void BuildSnapLine()
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();

        // A 4 m belt from x = -6 to -2 along +X, with an emitter over its start.
        SceneItem? first = Editor.Place("belt_conveyor_4m", -4.0f, 0.0f);
        SceneItem? emitter = Editor.Place("emitter_gantry", -5.5f, 0.0f);
        bool low = SetParam("highRatio", 0.0);
        SnapCheck("first belt and emitter", first is not null && emitter is not null && low);

        // The curve, placed far away with yaw 0; without clicking it lands on the grid and keeps its yaw.
        SceneItem? curve = Editor.Place("belt_conveyor_curved", 3.0f, 3.0f);
        PortSnap? none = Editor.DragSelectedTo(-1.9f, -0.9f, clickOn: false);
        SceneItem? onGrid = curve is null ? null : Main.Scene.Find(curve.Id);
        SnapCheck($"Alt: no click, on the grid at ({onGrid?.X}, {onGrid?.Z}) yaw {onGrid?.Yaw}",
            none is null && onGrid is { X: -2.0, Z: -1.0, Yaw: 0.0 } && !Editor.SnapDotVisible);
        Editor.Undo();
        onGrid = curve is null ? null : Main.Scene.Find(curve.Id);
        SnapCheck("undo puts the curve back", onGrid is { X: 3.0, Z: 3.0, Yaw: 0.0 });

        // Dragged near: it turns 270 degrees and its in meets the out of the first belt at (-2, 0).
        Editor.Select(curve?.Id);
        PortSnap? clicked = Editor.DragSelectedTo(-1.9f, -0.9f);
        SceneItem? snapped = curve is null ? null : Main.Scene.Find(curve.Id);
        SnapCheck($"curve clicks on: ({snapped?.X}, {snapped?.Z}) yaw {snapped?.Yaw}, turned {clicked?.TurnDegrees}",
            clicked is { TurnDegrees: 270.0, OtherId: var other } && other == first?.Id && snapped is { X: -2.0, Z: -1.0, Yaw: 270.0 });
        Editor.Undo();
        bool undone = curve is not null && Main.Scene.Find(curve.Id) is { X: 3.0, Z: 3.0, Yaw: 0.0 };
        Editor.Redo();
        bool redone = curve is not null && Main.Scene.Find(curve.Id) is { X: -2.0, Z: -1.0, Yaw: 270.0 };
        SnapCheck("one undo step for drag and click", undone && redone);

        // From the library: a 2 m belt along -Z, held near the out of the curve at (-1, -1).
        CatalogAsset belt = Main.Catalog.Get("belt_conveyor_2m");
        PortSnap? fromLibrary = ScenePorts.SnapNew(Main.Scene, Main.Catalog, belt, 90.0, -0.85, -2.1);
        SceneItem? second = Editor.Place(belt.Id, -0.85f, -2.1f, 90.0f, fromLibrary);
        SnapCheck($"belt from the library clicks on: ({second?.X}, {second?.Z}) yaw {second?.Yaw}",
            fromLibrary is not null && second is { X: -1.0, Z: -2.0, Yaw: 90.0 });

        // Every end meets exactly: out of the first = in of the curve, out of the curve = in of the second.
        bool ends = first is not null && curve is not null && second is not null
            && Meets(first.Id, curve.Id) && Meets(curve.Id, second.Id);
        SnapCheck("ends meet to the millimetre", ends);

        SceneItem? remover = Editor.Place("remover_hood_1m0", -1.0f, -3.25f, 90.0f);
        SnapCheck($"no overlap markers on the clicked line ({Editor.OverlapMarkerCount}{(Editor.OverlapWarning.Length > 0 ? ": " + Editor.OverlapWarning : "")})",
            Editor.OverlapMarkerCount == 0 && Editor.OverlapWarning.Length == 0);
        Editor.SetDesignMode(false);
        _snEmitter = emitter?.Name ?? string.Empty;
        SnapCheck("remover and tags", remover is not null && Main.Tags.ActuatorNames.Contains(_snEmitter));
        if (!_snHooked)
        {
            _snHooked = true;
            Main.View.ItemEmitted += (_, _) => _snEmitted++;
            Main.View.ItemRemoved += (_, _) => _snRemoved++;
        }

        Main.Camera.Frame(new Vector3(-2.5f, 0.8f, -1.5f), 9.0f);
        Main.Sim.Run();
    }

    /// <summary>True when an out of <paramref name="fromId"/> lies within a millimetre of an in of <paramref name="toId"/>.</summary>
    private bool Meets(string fromId, string toId)
    {
        SceneItem from = Main.Scene.Find(fromId)!;
        SceneItem to = Main.Scene.Find(toId)!;
        var outs = ScenePorts.Ports(from, Main.Catalog.Get(from.Asset)).Where(p => !p.IsIn);
        var ins = ScenePorts.Ports(to, Main.Catalog.Get(to.Asset)).Where(p => p.IsIn).ToArray();
        return outs.Any(o => ins.Any(i => Math.Abs(o.X - i.X) < 0.001 && Math.Abs(o.Z - i.Z) < 0.001
            && Math.Abs(o.Y - i.Y) < 0.001 && o.DirX * i.DirX + o.DirZ * i.DirZ > 0.999));
    }

    private void SnapCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST snap {what}: {(ok ? "ok" : "FAILED")}");
        _snPass &= ok;
    }
}
