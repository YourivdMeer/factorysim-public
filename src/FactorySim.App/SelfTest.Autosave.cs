using System;
using System.IO;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 16, part of the "editor" self-test: automatic copies in a temporary folder of their own.
/// The times in the names are fixed by the test, so it does not wait two minutes.
/// </summary>
public partial class SelfTest
{
    /// <summary>Runs on the scene that was just imported from <paramref name="sceneFile"/>; leaves it saved there.</summary>
    private bool RunEditorAutosave(string sceneFile)
    {
        // Every self-test runs with automatic saving off; only this one switches it on, in a folder of its own.
        bool offByDefault = Editor.AutosaveFolder is null;
        string folder = Path.Combine(Path.GetTempPath(), "factorysim_autosave_test");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Editor.AutosaveFolder = folder;
        DateTime start = DateTime.Now.AddMinutes(1);         // later than the scene file

        // 1. A change, then a copy: exactly one.
        SceneItem? column = Editor.Place("column", -3.0f, 3.0f);
        string? first = Editor.AutosaveNow(start);
        bool one = column is not null && first is not null && Editor.AutosaveCopies().Count == 1
            && Directory.GetFiles(folder).Length == 1;                       // no .tmp left behind

        // 2. Again without a change: no new copy.
        bool unchanged = Editor.AutosaveNow(start.AddMinutes(2)) is null && Editor.AutosaveCopies().Count == 1;

        // 3. Three more changes, each with a copy: the newest three stay.
        (float X, float Z)[] moves = { (-3.0f, 3.5f), (-3.5f, 3.5f), (-3.5f, 3.0f) };
        string? newest = null;
        for (int i = 0; i < moves.Length; i++)
        {
            Editor.MoveSelected(moves[i].X, moves[i].Z);
            newest = Editor.AutosaveNow(start.AddMinutes(4 + 2 * i));
        }

        var copies = Editor.AutosaveCopies();
        bool three = copies.Count == 3 && newest is not null && copies[0] == newest && !copies.Contains(first);

        // The same file name in another folder is another scene: its copies are not these.
        string twin = "user://selftest/" + Path.GetFileName(sceneFile);
        bool apart = AutosavePolicy.Key(twin) != AutosavePolicy.Key(sceneFile) && Editor.AutosaveToRestore(twin) is null;

        // 4. Open the file again (no question in a self-test), then restore: the scene of the newest copy.
        string newestJson = newest is null ? string.Empty : File.ReadAllText(newest);
        bool reopened = Editor.LoadFrom(sceneFile) is null && Main.Scene.ToJson() != newestJson;
        string? offered = Editor.AutosaveToRestore(sceneFile);
        bool restored = offered == newest && offered is not null && Editor.RestoreAutosave(offered) is null
            && Main.Scene.ToJson() == newestJson && Main.CurrentScenePath == sceneFile
            && Editor.AutosaveCopies().Count == 3 && !Editor.ChangedSinceSave;

        // 5. A normal save: the copies of this scene are gone.
        bool cleared = Editor.SaveTo(sceneFile) is null && Editor.AutosaveCopies().Count == 0
            && Directory.GetFiles(folder).Length == 0;

        Editor.AutosaveFolder = null;
        Directory.Delete(folder, recursive: true);
        bool pass = offByDefault && one && unchanged && three && apart && reopened && restored && cleared;
        GD.Print($"SELFTEST editor autosave: offByDefault={offByDefault} one={one} unchanged={unchanged} three={three} "
            + $"apart={apart} reopened={reopened} restored={restored} cleared={cleared}");
        return pass;
    }
}
