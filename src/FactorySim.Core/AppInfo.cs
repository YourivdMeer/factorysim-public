namespace FactorySim.Core;

/// <summary>
/// The version of FactorySim and the fixed texts of the window <i>Over</i> (plan 28). The version is also in
/// <c>config/version</c> in <c>project.godot</c> and at the top of <c>WIJZIGINGEN.txt</c>; <c>AppInfoTests</c> keeps them equal.
/// </summary>
public static class AppInfo
{
    public const string Name = "FactorySim";

    public const string Version = "0.1.0-beta.1";

    public const string NetworkNote =
        "FactorySim verstuurt niets naar internet. De koppeling met de PLC (Modbus TCP) en de test-API luisteren of verbinden "
        + "alleen op deze pc (127.0.0.1), tenzij je zelf een andere computer kiest.";

    public const string TrademarkNote = "CODESYS is een merk van CODESYS GmbH; FactorySim heeft geen band met CODESYS GmbH.";

    public const string LicenceNote =
        "FactorySim gebruikt Godot Engine, .NET en NModbus. Hun licenties staan in THIRD-PARTY-NOTICES.txt naast het programma.";

    /// <summary>"FactorySim 0.1.0-beta.1 — &lt;scene&gt;", or without the scene when it has no name.</summary>
    public static string WindowTitle(string? sceneName) =>
        string.IsNullOrWhiteSpace(sceneName) ? $"{Name} {Version}" : $"{Name} {Version} — {sceneName.Trim()}";
}
