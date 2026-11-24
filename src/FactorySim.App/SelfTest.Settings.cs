using System;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App;

/// <summary>Plan 15, part of the "ui" self-test: settings written, read back and applied, with a temporary file.</summary>
public partial class SelfTest
{
    private const string SettingsTestPath = "user://selftest_settings.cfg";

    private void RunUiSettings()
    {
        // A self-test never reads or writes the real settings file.
        SortersCheck("self-test keeps away from settings.cfg", Main.SettingsPath is null);

        var camera = new CameraState(1.25, 0.5, -0.75, 11.5, -60.0, -40.0);
        var written = new AppSettings
        {
            ScenePath = "user://scenes/andere_scene.fsscene.json",
            Port = 5020,
            UnitId = 1,
            CameraScene = Main.CurrentScenePath,
            Camera = camera,
            BottomTab = 2,
        }.WithRecent("user://scenes/andere_scene.fsscene.json");

        // 1 and 2: write, read back, apply.
        bool saved = SettingsFile.Save(SettingsTestPath, written) is null;
        AppSettings read = SettingsFile.Load(SettingsTestPath);
        Main.ApplySettings(read, includeWindow: false);
        (string host, int port, int unit) = Main.Hud.PlcFields;
        OrbitCamera view = Main.Camera;
        bool plc = host == "127.0.0.1" && port == 5020 && unit == 1;
        bool cameraOk = view.Pivot.DistanceTo(new Vector3(1.25f, 0.5f, -0.75f)) < 0.001f && Math.Abs(view.Distance - 11.5f) < 0.001f
            && Math.Abs(view.YawDegrees + 60.0f) < 0.01f && Math.Abs(view.PitchDegrees + 40.0f) < 0.01f;
        bool panel = Main.Hud.BottomTab == 2 && !Main.Hud.BottomCollapsed;
        AppSettings captured = Main.CaptureSettings();
        bool capture = captured.Port == 5020 && captured.Camera == camera && captured.BottomTab == 2
            && captured.Recent.Count == 1 && read.ScenePath == written.ScenePath;
        GD.Print($"SELFTEST ui settings: saved={saved} plc={host}:{port}/{unit} camera={view.Pivot} {view.Distance} {view.YawDegrees} {view.PitchDegrees} tab={Main.Hud.BottomTab}");
        SortersCheck("settings written, read back and applied", saved && plc && cameraOk && panel && capture);

        // The camera of another scene is not applied.
        Main.Camera.Frame(Vector3.Zero, 7.0f);
        Main.ApplySettings(read with { CameraScene = "user://scenes/iets_anders.fsscene.json" }, includeWindow: false);
        SortersCheck("camera only for its own scene", Main.Camera.Pivot == Vector3.Zero);

        // 3: a broken port falls back to 502, the rest stays.
        var file = new ConfigFile();
        file.Load(SettingsTestPath);
        file.SetValue("factorysim", "plc.port", "abc");
        file.Save(SettingsTestPath);
        AppSettings broken = SettingsFile.Load(SettingsTestPath);
        SortersCheck("broken port falls back, the rest stays", broken.Port == 502 && broken.UnitId == 1
            && broken.Camera == camera && broken.ScenePath == written.ScenePath && broken.BottomTab == 2);

        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SettingsTestPath));
        Main.ApplySettings(new AppSettings(), includeWindow: false);
    }
}
