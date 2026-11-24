using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FactorySim.Core.Scenes;

/// <summary>
/// Plan 16: the rules for automatic copies of a scene. A copy is written every two minutes of real
/// time while designing, only when the scene changed. It is called
/// <c>&lt;name&gt;.&lt;hash&gt;.&lt;yyyyMMdd-HHmmss&gt;.fsscene.json</c>: the name of the scene file, a
/// short hash of its full path (so an example and an own scene with the same name keep apart) and
/// the local time. The newest three copies of a scene stay.
/// </summary>
public static class AutosavePolicy
{
    public const long IntervalMs = 120_000;
    public const int Keep = 3;
    public const string Extension = ScenePaths.Extension;
    public const string TimeFormat = "yyyyMMdd-HHmmss";

    /// <summary>True when two minutes have passed since the last save or copy and the scene changed.</summary>
    public static bool IsDue(long nowMs, long lastSaveMs, bool changed) => changed && IsTimeDue(nowMs, lastSaveMs);

    /// <summary>Only the clock part of <see cref="IsDue"/>: lets the caller skip comparing the scene every frame.</summary>
    public static bool IsTimeDue(long nowMs, long lastSaveMs) => nowMs - lastSaveMs >= IntervalMs;

    /// <summary>Letters, digits, '-' and '_' stay; everything else becomes '_'. An empty name becomes "scene".</summary>
    public static string SafeName(string name)
    {
        string safe = new(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return safe.Length == 0 ? "scene" : safe;
    }

    /// <summary>FNV-1a (32 bit) of the path with '/' as separator and in lower case, as 8 hex digits. Stable between runs.</summary>
    public static string PathHash(string path)
    {
        string normal = path.Replace('\\', '/').ToLowerInvariant();
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(normal))
        {
            hash ^= b;
            hash *= 16777619;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>What the copies of the scene at this path have in common: the safe file name and the hash of the path.</summary>
    public static string Key(string scenePath)
    {
        int slash = Math.Max(scenePath.LastIndexOf('/'), scenePath.LastIndexOf('\\'));
        string file = ScenePaths.WithoutExtension(scenePath[(slash + 1)..]);

        return $"{SafeName(file)}.{PathHash(scenePath)}";
    }

    /// <summary>The file name of a copy of the scene at this path, written at this (local) time.</summary>
    public static string FileName(string scenePath, DateTime time) =>
        $"{Key(scenePath)}.{time.ToString(TimeFormat, CultureInfo.InvariantCulture)}{Extension}";

    /// <summary>Reads a copy's file name (or full path) back; null for any other file, also a half-written ".tmp".</summary>
    public static AutosaveCopy? Parse(string fileName)
    {
        int slash = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
        string name = fileName[(slash + 1)..];
        if (!name.EndsWith(Extension, StringComparison.Ordinal) && !name.EndsWith(ScenePaths.LegacyExtension, StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = ScenePaths.WithoutExtension(name).Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length != 8 || !parts[1].All(Uri.IsHexDigit)
            || !DateTime.TryParseExact(parts[2], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return null;
        }

        return new AutosaveCopy(name, $"{parts[0]}.{parts[1]}", time);
    }

    /// <summary>The copies of one scene among these files, newest first.</summary>
    public static IReadOnlyList<AutosaveCopy> CopiesOf(IEnumerable<string> files, string key) =>
        files.Select(Parse).Where(c => c is not null && c.Key == key).Select(c => c!)
            .OrderByDescending(c => c.Time).ThenByDescending(c => c.FileName, StringComparer.Ordinal).ToList();

    /// <summary>The file names of the copies of this scene that may go: all but the newest <paramref name="keep"/>.</summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> files, string key, int keep = Keep) =>
        CopiesOf(files, key).Skip(Math.Max(0, keep)).Select(c => c.FileName).ToList();

    /// <summary>The newest copy of this scene written after the scene file, or null.</summary>
    public static AutosaveCopy? NewestNewerThan(IEnumerable<string> files, string key, DateTime sceneFileTime) =>
        CopiesOf(files, key).FirstOrDefault(c => c.Time > sceneFileTime);
}
