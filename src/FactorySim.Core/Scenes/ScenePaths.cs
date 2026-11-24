using System;
using System.IO;

namespace FactorySim.Core.Scenes;

/// <summary>
/// Plan 22: a level became a scene. Scene files end in <c>.fsscene.json</c> and live in <c>scenes/</c>; files and
/// folders from before (<c>.fslevel.json</c> in <c>levels/</c>) keep opening. Saving always writes the new extension.
/// </summary>
public static class ScenePaths
{
    public const string Extension = ".fsscene.json";
    public const string LegacyExtension = ".fslevel.json";
    public const string ExamplesFolder = "res://scenes";
    public const string OwnFolder = "user://scenes";
    public const string LegacyExamplesFolder = "res://levels";
    public const string LegacyOwnFolder = "user://levels";

    /// <summary>Written in the new own folder once the old one has been copied, so a removed scene does not come back.</summary>
    public const string MigratedMarker = ".migrated-from-levels";

    /// <summary>True for a scene file of either extension.</summary>
    public static bool IsSceneFile(string name) =>
        name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || name.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name without <see cref="Extension"/> or <see cref="LegacyExtension"/>; unchanged for other names.</summary>
    public static string WithoutExtension(string name)
    {
        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return name[..^Extension.Length];
        }

        return name.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase) ? name[..^LegacyExtension.Length] : name;
    }

    /// <summary>The path a save writes to: the same file name with <see cref="Extension"/>.</summary>
    public static string WithSceneExtension(string path) => WithoutExtension(path) + Extension;

    /// <summary>
    /// An example or own scene path from before plan 22 (<c>res://levels/x.fslevel.json</c>, <c>user://levels/…</c>) as
    /// it is now (<c>res://scenes/x.fsscene.json</c>). Every other path, also an exported file, stays as it is.
    /// </summary>
    public static string Upgrade(string path)
    {
        if (!path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        foreach ((string old, string fresh) in new[] { (LegacyExamplesFolder, ExamplesFolder), (LegacyOwnFolder, OwnFolder) })
        {
            if (path.StartsWith(old + "/", StringComparison.Ordinal) && path.IndexOf('/', old.Length + 1) < 0)
            {
                return fresh + "/" + WithSceneExtension(path[(old.Length + 1)..]);
            }
        }

        return path;
    }

    /// <summary>The path an example or own scene had before plan 22, or null for any other path; autosave copies are keyed on it.</summary>
    public static string? Legacy(string path)
    {
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach ((string old, string fresh) in new[] { (LegacyExamplesFolder, ExamplesFolder), (LegacyOwnFolder, OwnFolder) })
        {
            if (path.StartsWith(fresh + "/", StringComparison.Ordinal) && path.IndexOf('/', fresh.Length + 1) < 0)
            {
                return old + "/" + WithoutExtension(path[(fresh.Length + 1)..]) + LegacyExtension;
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the <c>.fslevel.json</c> files of the old own folder into the new one as <c>.fsscene.json</c>, once:
    /// afterwards <see cref="MigratedMarker"/> is in the new folder. A file that is already there is never overwritten;
    /// the old folder stays as it is. Returns how many files were copied.
    /// </summary>
    public static int MigrateFolder(string oldFolder, string newFolder)
    {
        string marker = Path.Combine(newFolder, MigratedMarker);
        if (!Directory.Exists(oldFolder) || File.Exists(marker))
        {
            return 0;
        }

        Directory.CreateDirectory(newFolder);
        int copied = 0;
        foreach (string file in Directory.GetFiles(oldFolder))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = Path.Combine(newFolder, WithSceneExtension(name));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
                copied++;
            }
        }

        File.WriteAllText(marker, "Plan 22: the files of the old folder 'levels' were copied here; that folder was left as it was.\n");
        return copied;
    }
}
