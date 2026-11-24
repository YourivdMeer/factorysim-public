using System.Globalization;

namespace FactorySim.Core.Diagnostics;

/// <summary>
/// Plan 31: what the app writes and says when it stops on an error. The crash file goes next to the log of Godot,
/// written with plain file I/O because the process is about to end and the log of Godot may not be flushed.
/// </summary>
public static class CrashReport
{
    public const string AlertTitle = "FactorySim is vastgelopen";

    public const string StartupTitle = "FactorySim kan niet starten";

    /// <summary>"crash-yyyyMMdd-HHmmss.txt".</summary>
    public static string FileName(DateTime when) => $"crash-{when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt";

    /// <summary>The content of the crash file: version, time and the whole exception with its stack.</summary>
    public static string Content(Exception error, DateTime when) =>
        $"{AppInfo.Name} {AppInfo.Version}{Environment.NewLine}"
        + $"Tijd: {when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}{Environment.NewLine}"
        + $"OS: {Environment.OSVersion}{Environment.NewLine}{Environment.NewLine}"
        + error;

    /// <summary>Writes the crash file in <paramref name="folder"/> (made when missing) and returns its path.</summary>
    public static string Write(string folder, Exception error, DateTime when)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName(when));
        File.WriteAllText(path, Content(error, when));
        return path;
    }

    /// <summary>The message box: what happened, where the files are, and the request to send them along.</summary>
    public static string AlertText(string? crashFile, string logFolder)
    {
        string where = crashFile is null
            ? $"De details staan in het log in deze map:\n{logFolder}"
            : $"De details staan in:\n{crashFile}\n\nHet log van de app staat in:\n{logFolder}";
        return $"FactorySim is gestopt door een fout in het programma.\n\n{where}\n\n"
            + $"Wil je dat bestand meesturen als je de fout meldt? Vermeld ook je versie: {AppInfo.Name} {AppInfo.Version}.";
    }

    /// <summary>The message box when the app cannot start (the catalogue or the first scene does not load).</summary>
    public static string StartupText(string reason, string logFolder) =>
        $"FactorySim kan niet starten:\n{reason}\n\nHet log staat in:\n{logFolder}\n\n"
        + $"Wil je het log meesturen als je de fout meldt? Versie: {AppInfo.Name} {AppInfo.Version}.";
}
