using System;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 17: a belt or curve dragged (or placed from the library) near the free end of another one clicks onto it.
/// The rules are in <see cref="ScenePorts"/>; here the editor shows a green dot where the ends meet and applies the
/// move, together with the drag, as one undo step. Alt switches clicking off.
/// </summary>
public partial class SceneEditor
{
    /// <summary>True while the green dot shows where a dragged or new belt would click on.</summary>
    public bool SnapDotVisible => _snapDot.Visible;

    /// <summary>
    /// Drags what is selected so that the selected item lands at (x, z), as a release of the mouse there would:
    /// clicked onto the end of another belt when one is in reach (unless <paramref name="clickOn"/> is false),
    /// otherwise on the grid. One undo step. Returns where it clicked on, or null.
    /// </summary>
    public PortSnap? DragSelectedTo(float x, float z, bool clickOn = true)
    {
        if (_selectedId is null || _main.Scene.Find(_selectedId) is not { } item)
        {
            return null;
        }

        PortSnap? snap = clickOn ? ScenePorts.Snap(_main.Scene, _main.Catalog, _selection, item.Id, x, z) : null;
        if (snap is not null)
        {
            ApplySnap(item.Id, snap);
        }
        else
        {
            MoveSelectedBy((float)(Scene.Snap(x) - item.X), (float)(Scene.Snap(z) - item.Z));
        }

        return snap;
    }

    /// <summary>Moves (and turns) the selection so that the anchor lands where it clicks on; one undo step.</summary>
    private void ApplySnap(string anchorId, PortSnap snap)
    {
        if (_main.Scene.Find(anchorId) is not { } anchor)
        {
            return;
        }

        string[] group = _selection.Append(anchorId).Distinct().ToArray();
        _history.Record(_main.Scene);
        _main.Scene.TransformMany(group, anchor.X, anchor.Z, snap.TurnDegrees, snap.X - anchor.X, snap.Z - anchor.Z);
        string? error = Rebuild();
        if (error is not null)
        {
            ShowStatus(error, isError: true);
            return;
        }

        string other = _main.Scene.Find(snap.OtherId)?.Name ?? snap.OtherId;
        ShowStatus($"{anchor.Name} vastgeklikt aan {other}. Alt+slepen klikt niet vast.");
    }

    /// <summary>Clicking on is on unless Alt is held.</summary>
    private static bool ClickingOn() => !Input.IsKeyPressed(Key.Alt);

    private void ShowSnapDot(PortSnap? snap)
    {
        _snapDot.Visible = snap is not null;
        if (snap is not null)
        {
            _snapDot.Position = new Vector3((float)snap.PortX, (float)snap.PortY + 0.05f, (float)snap.PortZ);
        }
    }

    /// <summary>A floor point turned about a pivot like an item's yaw: a positive angle takes +X towards -Z.</summary>
    private static Vector3 Turned(Vector3 point, Vector3 pivot, float degrees)
    {
        if (degrees == 0.0f)
        {
            return point;
        }

        double angle = degrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        float rx = point.X - pivot.X;
        float rz = point.Z - pivot.Z;
        return new Vector3(pivot.X + rx * cos + rz * sin, point.Y, pivot.Z - rx * sin + rz * cos);
    }
}
