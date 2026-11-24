using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 16: automatic copies of the scene while designing. Every two minutes of real time
/// (<see cref="Time.GetTicksMsec"/>, not the scaled delta) and on leaving design mode, when the scene
/// differs from the last save or copy, a copy goes to <c>user://autosave</c>. The newest three per scene
/// stay; a normal save removes them. On opening a scene a newer copy is offered for restore.
/// Self-tests switch this off; the editor self-test switches it on with a folder of its own.
/// </summary>
public partial class SceneEditor
{
    /// <summary>Where the app keeps its automatic copies.</summary>
    public const string DefaultAutosaveFolder = "user://autosave";

    private string _savedJson = string.Empty;
    private ulong _lastSaveMs;
    private ConfirmationDialog _autosaveDialog = null!;
    private string? _offeredCopy;

    /// <summary>The folder for automatic copies (user://, or a file system path); null switches automatic saving off.</summary>
    public string? AutosaveFolder { get; set; }

    /// <summary>The scene file the copies are named after: the file it came from, or where Opslaan would write it.</summary>
    public string AutosaveScenePath => _main.CurrentScenePath ?? DefaultSavePath();

    /// <summary>True when the scene differs from the last save, load or copy.</summary>
    public bool ChangedSinceSave => _main.Scene.ToJson() != _savedJson;

    /// <summary>The copies of the current scene in the folder, newest first (full paths).</summary>
    public IReadOnlyList<string> AutosaveCopies()
    {
        string? folder = AutosaveFolderPath();
        return folder is null
            ? Array.Empty<string>()
            : AutosavePolicy.CopiesOf(ListFolder(folder), AutosavePolicy.Key(AutosaveScenePath))
                .Select(c => Path.Combine(folder, c.FileName)).ToList();
    }

    /// <summary>
    /// Writes a copy now when the scene changed. <paramref name="at"/> fixes the time in the name (the
    /// self-test); without it the copy gets the current time. Returns the full path, or null when
    /// nothing was written.
    /// </summary>
    public string? AutosaveNow(DateTime? at = null)
    {
        string? folder = AutosaveFolderPath();
        if (folder is null)
        {
            return null;
        }

        _lastSaveMs = Time.GetTicksMsec();
        string json = _main.Scene.ToJson();
        if (json == _savedJson)
        {
            return null;
        }

        string scenePath = AutosaveScenePath;
        DateTime time = at ?? DateTime.Now;
        string target = Path.Combine(folder, AutosavePolicy.FileName(scenePath, time));
        while (at is null && File.Exists(target))
        {
            time = time.AddSeconds(1);          // two copies within one second: keep both
            target = Path.Combine(folder, AutosavePolicy.FileName(scenePath, time));
        }

        try
        {
            Directory.CreateDirectory(folder);
            // Write next to it and rename, so a crash while writing leaves no half copy.
            string temporary = target + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, target, overwrite: true);
            foreach (string old in AutosavePolicy.ToDelete(ListFolder(folder), AutosavePolicy.Key(scenePath)))
            {
                File.Delete(Path.Combine(folder, old));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Automatisch opslaan mislukt: {e.Message}", isError: true);
            return null;
        }

        _savedJson = json;
        ShowStatus($"Automatisch opgeslagen om {time.ToString("HH:mm", CultureInfo.InvariantCulture)}");
        return target;
    }

    /// <summary>The newest copy of the scene at this path that is newer than the file itself, or null.</summary>
    public string? AutosaveToRestore(string scenePath)
    {
        string? folder = AutosaveFolderPath();
        if (folder is null)
        {
            return null;
        }

        ulong modified = Godot.FileAccess.FileExists(scenePath) ? Godot.FileAccess.GetModifiedTime(scenePath) : 0;
        DateTime fileTime = modified == 0
            ? DateTime.MinValue
            : DateTimeOffset.FromUnixTimeSeconds((long)modified).LocalDateTime;
        string[] files = ListFolder(folder).ToArray();
        AutosaveCopy? copy = AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(scenePath), fileTime);
        if (copy is null && ScenePaths.Legacy(scenePath) is { } legacy)
        {
            copy = AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(legacy), fileTime);     // plan 22: a copy from before the rename
        }

        return copy is null ? null : Path.Combine(folder, copy.FileName);
    }

    /// <summary>
    /// Makes the copy the current scene. The scene keeps its own file (so the next Opslaan writes to the
    /// own folder as usual) and the copy stays until then. Returns an error text, or null.
    /// </summary>
    public string? RestoreAutosave(string copyPath)
    {
        Scene scene;
        try
        {
            scene = Scene.FromJson(File.ReadAllText(copyPath));
        }
        catch (Exception e) when (e is SceneException or IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Herstellen mislukt: {e.Message}", isError: true);
            return e.Message;
        }

        Select(null);
        string? error = _main.ApplyScene(scene);
        if (error is not null)
        {
            ShowStatus($"Herstellen mislukt: {error}", isError: true);
            return error;
        }

        _history.Clear();
        UpdateHistoryButtons();
        _sceneName.Text = _main.Scene.Name;
        MarkSaved();
        AutosaveCopy? copy = AutosavePolicy.Parse(copyPath);
        ShowStatus($"Automatisch opgeslagen versie hersteld{(copy is null ? "" : $" ({Stamp(copy.Time)})")}. Nog niet opgeslagen.");
        return null;
    }

    /// <summary>Removes all copies of the scenes at these paths.</summary>
    private void ClearAutosaves(params string[] scenePaths)
    {
        string? folder = AutosaveFolderPath();
        if (folder is null)
        {
            return;
        }

        foreach (string key in scenePaths.Select(AutosavePolicy.Key).Distinct())
        {
            foreach (string old in AutosavePolicy.ToDelete(ListFolder(folder), key, keep: 0))
            {
                try
                {
                    File.Delete(Path.Combine(folder, old));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    GD.PushWarning($"Cannot delete {old}: {e.Message}");
                }
            }
        }
    }

    /// <summary>The scene as it is now counts as saved: no copy until it changes.</summary>
    private void MarkSaved()
    {
        _savedJson = _main.Scene.ToJson();
        _lastSaveMs = Time.GetTicksMsec();
    }

    private void BuildAutosave()
    {
        AutosaveFolder = _main.SelfTestRun ? null : DefaultAutosaveFolder;
        _autosaveDialog = new ConfirmationDialog
        {
            Title = "Automatisch opgeslagen",
            OkButtonText = "Herstellen",
            CancelButtonText = "Niet nu",
        };
        _autosaveDialog.Confirmed += () =>
        {
            if (_offeredCopy is not null)
            {
                RestoreAutosave(_offeredCopy);
            }

            _offeredCopy = null;
        };
        _autosaveDialog.Canceled += () => _offeredCopy = null;
        AddChild(_autosaveDialog);
        MarkSaved();
        Callable.From(OfferAutosave).CallDeferred();      // the scene of the start
    }

    /// <summary>Asks whether to restore a newer copy of the current scene. Never in a self-test.</summary>
    private void OfferAutosave()
    {
        if (_main.SelfTestRun || _main.CurrentScenePath is not { } path)
        {
            return;
        }

        string? copy = AutosaveToRestore(path);
        if (copy is null)
        {
            return;
        }

        _offeredCopy = copy;
        AutosaveCopy? parsed = AutosavePolicy.Parse(copy);
        _autosaveDialog.DialogText = $"Er is een automatisch opgeslagen versie van {(parsed is null ? "onbekend" : Stamp(parsed.Time))}. Herstellen?";
        _autosaveDialog.PopupCentered();
    }

    /// <summary>Every frame: in design mode, after two minutes of real time, write a copy if the scene changed.</summary>
    private void PollAutosave()
    {
        if (!DesignMode || AutosaveFolder is null || !AutosavePolicy.IsTimeDue((long)Time.GetTicksMsec(), (long)_lastSaveMs))
        {
            return;
        }

        if (ChangedSinceSave)
        {
            AutosaveNow();
        }
        else
        {
            _lastSaveMs = Time.GetTicksMsec();          // look again in two minutes
        }
    }

    private void AutosaveIfChanged()
    {
        if (AutosaveFolder is not null && ChangedSinceSave)
        {
            AutosaveNow();
        }
    }

    /// <summary>The full path of <see cref="AutosaveFolder"/>, or null when automatic saving is off.</summary>
    public string? AutosaveFolderPath() =>
        AutosaveFolder is null ? null
        : AutosaveFolder.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(AutosaveFolder)
        : AutosaveFolder;

    private static IEnumerable<string> ListFolder(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder).Select(Path.GetFileName).Select(f => f!) : Array.Empty<string>();

    private static string Stamp(DateTime time) => time.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
}
