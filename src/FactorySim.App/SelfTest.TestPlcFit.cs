using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 34, part of the "ui" self-test: the button "Ingebouwde test-PLC" is off, with the reason as tooltip, in a
/// scene that does not fit the sorter, and on in the MVP scene. A press from code on the disabled button starts
/// nothing. The scene of before is put back afterwards.
/// </summary>
public partial class SelfTest
{
    private void RunUiTestPlcFit()
    {
        string before = Main.Scene.ToJson();
        Godot.Button button = Main.Hud.TestPlcButton;

        string? error = Main.LoadSceneFile("res://scenes/assembly_line.fsscene.json");
        bool off = error is null && button.Disabled && button.TooltipText.Contains("MVP-scène", System.StringComparison.Ordinal);
        GD.Print($"SELFTEST ui test-PLC in assembly_line: disabled={button.Disabled} tooltip=\"{button.TooltipText}\"");
        SortersCheck("test-PLC button off with a reason in another scene", off);

        button.EmitSignal(BaseButton.SignalName.Pressed);
        SortersCheck("test-PLC does not start in another scene", Main.TestPlc is null);

        error = Main.LoadSceneFile("res://scenes/mvp_sorting.fsscene.json");
        SortersCheck("test-PLC button on in the MVP scene", error is null && !button.Disabled);

        Main.ApplyScene(Scene.FromJson(before));
    }
}
