using System.Collections.Generic;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> with Godot's <see cref="ConfigFile"/> (plan 15).
/// All values are stored as text in one section, so the rules for broken values stay in Core.
/// </summary>
public static class SettingsFile
{
    /// <summary>Where the app keeps its settings. Self-tests never read or write this file.</summary>
    public const string DefaultPath = "user://settings.cfg";

    private const string Section = "factorysim";

    /// <summary>Reads the file; a missing or unreadable file gives the defaults.</summary>
    public static AppSettings Load(string path)
    {
        var file = new ConfigFile();
        if (!FileAccess.FileExists(path) || file.Load(path) != Error.Ok || !file.HasSection(Section))
        {
            return new AppSettings();
        }

        var pairs = new Dictionary<string, string>();
        foreach (string key in file.GetSectionKeys(Section))
        {
            pairs[key] = file.GetValue(Section, key).AsString();
        }

        return AppSettings.FromPairs(pairs);
    }

    /// <summary>Writes the file. Returns an error text, or null.</summary>
    public static string? Save(string path, AppSettings settings)
    {
        var file = new ConfigFile();
        foreach ((string key, string value) in settings.ToPairs())
        {
            file.SetValue(Section, key, value);
        }

        Error error = file.Save(path);
        return error == Error.Ok ? null : error.ToString();
    }
}
