using FactorySim.App.Ui;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 30: the welcome window at the first start (no settings file yet), and at every start after that until
/// "Niet meer tonen". A self-test never reads the settings, so it never sees the window at the start.
/// </summary>
public partial class Main
{
    /// <summary>The welcome window.</summary>
    public WelcomeDialog WelcomeWindow => _hud.WelcomeWindow;

    /// <summary>True when the welcome window was opened at this start.</summary>
    public bool WelcomedAtStart { get; private set; }

    /// <summary>What <see cref="Welcome.ShowAtStart"/> says for a settings file at <paramref name="path"/> (also when it is missing).</summary>
    public static bool WelcomeAtStart(string path) => Welcome.ShowAtStart(FileAccess.FileExists(path), SettingsFile.Load(path));

    /// <summary>Hooks up "Niet meer tonen" and, outside a self-test, opens the window when it is due.</summary>
    private void SetUpWelcome()
    {
        _hud.WelcomeWindow.DontShowAgain += () => _settings = Welcome.Dismissed(_settings);
        if (_settingsPath is null || !Welcome.ShowAtStart(_settingsFileExisted, _settings))
        {
            return;
        }

        // Closing with "Sluiten" shows it again next time; the settings poll writes this within two seconds.
        _settings = Welcome.Shown(_settings);
        WelcomedAtStart = true;
        Callable.From(() => _hud.WelcomeWindow.PopupCentered()).CallDeferred();
    }
}
