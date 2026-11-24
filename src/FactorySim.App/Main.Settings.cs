using System;
using FactorySim.Core.Scenes;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 15: the app starts where you left it. The settings are polled once a second and written
/// when they have changed and then stayed the same for a second, and once more when the window
/// closes. Self-tests use the defaults and never read or write the file.
/// </summary>
public partial class Main
{
    private const ulong SettingsPollMs = 1000;

    private string? _settingsPath;
    private AppSettings _settings = new();
    private AppSettings _writtenSettings = new();
    private AppSettings? _lastPolled;
    private ulong _nextSettingsPoll;
    private bool _settingsFileExisted;

    /// <summary>The file the settings are kept in; null in a self-test, which never reads or writes it.</summary>
    public string? SettingsPath => _settingsPath;

    /// <summary>True when the app runs a self-test: nothing is asked, and nothing is written to the user's folders.</summary>
    public bool SelfTestRun { get; private set; }

    /// <summary>Recently opened or saved scene files, newest first (plan 15); the scene gallery shows them.</summary>
    public System.Collections.Generic.IReadOnlyList<string> RecentScenes => _settings.Recent;

    /// <summary>The scene file the current scene came from or was saved to; null for a new, unsaved scene.</summary>
    public string? CurrentScenePath { get; private set; }

    /// <summary>The editor opened or saved a scene file (or started a new scene: null).</summary>
    public void NoteScenePath(string? path)
    {
        CurrentScenePath = path;
        if (path is not null)
        {
            _settings = _settings.WithRecent(path);
        }
    }

    /// <summary>The settings as the app is now: scene, PLC fields, camera, bottom panel, window and recent scenes.</summary>
    public AppSettings CaptureSettings() => _settings with
    {
        ScenePath = CurrentScenePath,
        Host = _hud.PlcFields.Host,
        Port = _hud.PlcFields.Port,
        UnitId = _hud.PlcFields.UnitId,
        CameraScene = CurrentScenePath,
        Camera = new CameraState(
            Round(Camera.Pivot.X), Round(Camera.Pivot.Y), Round(Camera.Pivot.Z),
            Round(Camera.Distance), Round(Camera.YawDegrees), Round(Camera.PitchDegrees)),
        BottomCollapsed = _hud.BottomCollapsed,
        BottomTab = _hud.BottomTab,
        Window = CaptureWindow(_settings.Window),
    };

    /// <summary>
    /// Puts settings into the app: the PLC fields (without connecting), the bottom panel, the camera when
    /// it belongs to the current scene, and the window when asked.
    /// </summary>
    public void ApplySettings(AppSettings settings, bool includeWindow)
    {
        _settings = settings;
        _hud.PlcFields = (settings.Host, settings.Port, settings.UnitId);
        _hud.BottomTab = settings.BottomTab;
        _hud.BottomCollapsed = settings.BottomCollapsed;
        if (settings.Camera is { } camera && settings.CameraScene == CurrentScenePath)
        {
            Camera.Frame(new Vector3((float)camera.PivotX, (float)camera.PivotY, (float)camera.PivotZ), (float)camera.Distance);
            Camera.SetView((float)camera.Yaw, (float)camera.Pitch);
        }

        if (includeWindow)
        {
            RestoreWindow(settings.Window);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && _ready && _settingsPath is not null)
        {
            WriteSettings(CaptureSettings());
        }
    }

    /// <summary>Reads the settings, unless this run is a self-test.</summary>
    private void LoadSettings(bool selfTest)
    {
        SelfTestRun = selfTest;
        _settingsPath = selfTest ? null : SettingsFile.DefaultPath;
        if (!selfTest)
        {
            MigrateOwnScenes();
        }

        _settingsFileExisted = _settingsPath is not null && FileAccess.FileExists(_settingsPath);
        _settings = _settingsPath is null ? new AppSettings() : SettingsFile.Load(_settingsPath);
        _writtenSettings = _settings;
    }

    /// <summary>Plan 22: copies the own scenes of before the rename into the scenes folder, once; the old folder stays.</summary>
    private static void MigrateOwnScenes()
    {
        try
        {
            int copied = ScenePaths.MigrateFolder(
                ProjectSettings.GlobalizePath(ScenePaths.LegacyOwnFolder), ProjectSettings.GlobalizePath(ScenePaths.OwnFolder));
            if (copied > 0)
            {
                GD.Print($"Plan 22: {copied} eigen scène(s) uit de oude map levels gekopieerd naar {ScenePaths.OwnFolder}.");
            }
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Kopiëren van de oude eigen levels naar {ScenePaths.OwnFolder} mislukt: {e.Message}");
        }
    }

    /// <summary>Once a second: write the settings when they changed and then stayed the same for a poll.</summary>
    private void PollSettings()
    {
        if (_settingsPath is null || !_ready || Time.GetTicksMsec() < _nextSettingsPoll)
        {
            return;
        }

        _nextSettingsPoll = Time.GetTicksMsec() + SettingsPollMs;
        AppSettings now = CaptureSettings();
        _settings = now;
        if (!now.SameAs(_writtenSettings) && _lastPolled is not null && now.SameAs(_lastPolled))
        {
            WriteSettings(now);
        }

        _lastPolled = now;
    }

    private void WriteSettings(AppSettings settings)
    {
        if (_settingsPath is null || settings.SameAs(_writtenSettings))
        {
            return;
        }

        string? error = SettingsFile.Save(_settingsPath, settings);
        if (error is null)
        {
            _writtenSettings = settings;
        }
        else
        {
            GD.PushWarning($"Cannot write {_settingsPath}: {error}");
        }
    }

    /// <summary>On leaving: write what the last poll saw; the window may already be going away.</summary>
    private void WriteSettingsOnExit()
    {
        if (_settingsPath is not null && _ready)
        {
            WriteSettings(_settings);
        }
    }

    private static bool Headless => DisplayServer.GetName() == "headless";

    /// <summary>The normal window; when maximized, the size it had before, so maximizing does not overwrite it.</summary>
    private static WindowState? CaptureWindow(WindowState? before)
    {
        if (Headless)
        {
            return before;
        }

        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        if (mode == DisplayServer.WindowMode.Maximized)
        {
            Vector2I size = DisplayServer.WindowGetSize();
            Vector2I position = DisplayServer.WindowGetPosition();
            return before is null
                ? new WindowState(position.X, position.Y, size.X, size.Y, Maximized: true)
                : before with { Maximized = true };
        }

        if (mode != DisplayServer.WindowMode.Windowed)
        {
            return before;          // minimized or full screen: keep what we had
        }

        Vector2I windowSize = DisplayServer.WindowGetSize();
        Vector2I windowPosition = DisplayServer.WindowGetPosition();
        return new WindowState(windowPosition.X, windowPosition.Y, windowSize.X, windowSize.Y, Maximized: false);
    }

    /// <summary>Restores the window, but only on a screen that is still there; otherwise the standard place.</summary>
    private static void RestoreWindow(WindowState? window)
    {
        if (window is null || Headless)
        {
            return;
        }

        if (IsOnAScreen(window))
        {
            DisplayServer.WindowSetSize(new Vector2I(window.Width, window.Height));
            DisplayServer.WindowSetPosition(new Vector2I(window.X, window.Y));
        }

        if (window.Maximized)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        }
    }

    /// <summary>True when the top of the window (where the title bar is) lies for at least half on one screen.</summary>
    private static bool IsOnAScreen(WindowState window)
    {
        var top = new Rect2I(window.X, window.Y, Math.Min(window.Width, 200), 40);
        for (int screen = 0; screen < DisplayServer.GetScreenCount(); screen++)
        {
            var area = new Rect2I(DisplayServer.ScreenGetPosition(screen), DisplayServer.ScreenGetSize(screen));
            if (area.Intersection(top).Area * 2 >= top.Area)
            {
                return true;
            }
        }

        return false;
    }

    private static double Round(float value) => Math.Round(value, 3);
}
