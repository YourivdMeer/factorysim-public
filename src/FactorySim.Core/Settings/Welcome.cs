namespace FactorySim.Core.Settings;

/// <summary>
/// Plan 30: when the welcome window opens at the start, and its three steps. It opens at the first start
/// (no settings file yet) and at every start after that until the user clicks <i>Niet meer tonen</i>.
/// A settings file from before plan 30 has no key, so a user who already knows the app is not welcomed.
/// Self-tests never read the settings and never show the window.
/// </summary>
public static class Welcome
{
    public const string Title = "Welkom bij FactorySim";

    public const string Intro =
        "FactorySim is een 3D-fabriek die je met een PLC-programma bestuurt. Zo zie je in een minuut een draaiende sorteerlijn:";

    /// <summary>The three steps, with the names of the buttons as they are in the app.</summary>
    public static readonly IReadOnlyList<string> Steps = new[]
    {
        "1. De voorbeeldscène \"MVP — sorteren op hoogte\" staat open. Andere scène open? Klik ✎ Ontwerpen → Scène ▾ → Openen… "
            + "en kies onder Voorbeelden \"MVP — sorteren op hoogte\"; klik daarna ▶ Simuleren.",
        "2. Klik rechtsboven op \"PLC: niet verbonden\" en dan op \"Ingebouwde test-PLC\". Die sorteert alleen de MVP-scène. "
            + "Heb je CODESYS Control Win V3? Vul dan de poort (502) in en klik Verbinden.",
        "3. Klik ▶ Run en daarna Start. De dozen komen op de band; de hoge gaan de zijbaan in.",
    };

    public const string Outro =
        "Dit venster vind je terug onder Help ▾ rechtsboven, \"Aan de slag\". De handleiding staat in HANDLEIDING.txt naast het programma.";

    /// <summary>True when the welcome window opens at this start.</summary>
    public static bool ShowAtStart(bool settingsFileExists, AppSettings settings) => !settingsFileExists || settings.ShowWelcome;

    /// <summary>The window was shown at a start and closed without "Niet meer tonen": show it again next time.</summary>
    public static AppSettings Shown(AppSettings settings) => settings with { ShowWelcome = true };

    /// <summary>"Niet meer tonen".</summary>
    public static AppSettings Dismissed(AppSettings settings) => settings with { ShowWelcome = false };
}
