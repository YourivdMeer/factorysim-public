using FactorySim.App.Ui;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 30, part of the "ui" self-test: the welcome window opens only with an empty settings folder (checked on a
/// temporary file, because a self-test never reads settings.cfg), "Aan de slag" opens it, and "Niet meer tonen"
/// clears the setting and closes it.
/// </summary>
public partial class SelfTest
{
    private const string WelcomeTestPath = "user://selftest_welcome.cfg";

    private void RunUiWelcome()
    {
        var window = Main.WelcomeWindow;
        SortersCheck("self-test starts without the welcome window", !Main.WelcomedAtStart && Main.SettingsPath is null);

        // Empty folder: shown. A file from before plan 30: not shown. After a first start: shown until dismissed.
        string absolute = ProjectSettings.GlobalizePath(WelcomeTestPath);
        DirAccess.RemoveAbsolute(absolute);
        bool empty = Main.WelcomeAtStart(WelcomeTestPath);
        SettingsFile.Save(WelcomeTestPath, new AppSettings { Port = 5020 });
        bool existing = Main.WelcomeAtStart(WelcomeTestPath);
        SettingsFile.Save(WelcomeTestPath, Welcome.Shown(new AppSettings()));
        bool again = Main.WelcomeAtStart(WelcomeTestPath);
        SettingsFile.Save(WelcomeTestPath, Welcome.Dismissed(Welcome.Shown(new AppSettings())));
        bool dismissed = Main.WelcomeAtStart(WelcomeTestPath);
        DirAccess.RemoveAbsolute(absolute);
        GD.Print($"SELFTEST ui welcome: empty={empty} existing={existing} again={again} dismissed={dismissed}");
        SortersCheck("welcome only with an empty settings folder (or until Niet meer tonen)", empty && !existing && again && !dismissed);

        // "Aan de slag" opens it; "Niet meer tonen" clears the setting and closes it.
        Main.ApplySettings(new AppSettings { ShowWelcome = true }, includeWindow: false);
        Main.Hud.WelcomeButton.EmitSignal(BaseButton.SignalName.Pressed);
        bool opened = window.Visible;
        bool steps = window.GetNode<Label>("Column/Step1").Text == Welcome.Steps[0]
            && window.GetNode<Label>("Column/Step3").Text == Welcome.Steps[2];
        window.EmitSignal(AcceptDialog.SignalName.CustomAction, WelcomeDialog.DontShowAction);
        bool closed = !window.Visible;
        bool cleared = !Main.CaptureSettings().ShowWelcome;
        GD.Print($"SELFTEST ui welcome: opened={opened} steps={steps} closed={closed} cleared={cleared}");
        SortersCheck("Aan de slag opens the welcome, Niet meer tonen clears it", opened && steps && closed && cleared);
        Main.ApplySettings(new AppSettings(), includeWindow: false);
    }
}
