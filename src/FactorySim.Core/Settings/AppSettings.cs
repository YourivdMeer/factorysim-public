using System.Globalization;
using FactorySim.Core.Scenes;

namespace FactorySim.Core.Settings;

/// <summary>
/// What the app remembers between sessions (plan 15): the last scene, the PLC fields, the camera,
/// the bottom panel and the window, plus the recently opened scenes and whether to welcome again (plan 30). Stored as key/value pairs so
/// Core knows nothing of the file format. A broken or unknown value falls back to the default for
/// that one key; the camera and the window count as one key each.
/// Not remembered on purpose: connecting to the PLC, the time scale, forces and the simulation state.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Length of the list of recently opened scenes.</summary>
    public const int MaxRecent = 10;

    /// <summary>Camera distance bounds; the same as in the app's orbit camera.</summary>
    public const double MinCameraDistance = 1.0;

    /// <inheritdoc cref="MinCameraDistance"/>
    public const double MaxCameraDistance = 40.0;

    /// <summary>Pitch bounds of the orbit camera, in degrees (negative is looking down).</summary>
    public const double MinCameraPitch = -89.0;

    /// <inheritdoc cref="MinCameraPitch"/>
    public const double MaxCameraPitch = -2.0;

    /// <summary>Number of tabs in the bottom panel (Tags, I/O-lijst, Trace, Cijfers).</summary>
    public const int BottomTabCount = 4;

    /// <summary>Smallest window that is restored, in pixels.</summary>
    public const int MinWindowWidth = 320;

    /// <inheritdoc cref="MinWindowWidth"/>
    public const int MinWindowHeight = 240;

    private const string KeyScene = "scene";
    private const string LegacyKeyScene = "level";
    private const string KeyHost = "plc.host";
    private const string KeyPort = "plc.port";
    private const string KeyUnit = "plc.unit";
    private const string KeyCameraScene = "camera.scene";
    private const string LegacyKeyCameraScene = "camera.level";
    private const string KeyCameraX = "camera.x";
    private const string KeyCameraY = "camera.y";
    private const string KeyCameraZ = "camera.z";
    private const string KeyCameraDistance = "camera.distance";
    private const string KeyCameraYaw = "camera.yaw";
    private const string KeyCameraPitch = "camera.pitch";
    private const string KeyCollapsed = "panel.collapsed";
    private const string KeyTab = "panel.tab";
    private const string KeyWindowX = "window.x";
    private const string KeyWindowY = "window.y";
    private const string KeyWindowWidth = "window.width";
    private const string KeyWindowHeight = "window.height";
    private const string KeyWindowMaximized = "window.maximized";
    private const string KeyRecent = "recent.";
    private const string KeyWelcome = "welcome.show";

    /// <summary>The scene that was last opened or saved (res://, user:// or a file system path); null for none.</summary>
    public string? ScenePath { get; init; }

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 502;

    public int UnitId { get; init; } = 255;

    /// <summary>The scene the camera was saved with; the camera is only restored for that scene.</summary>
    public string? CameraScene { get; init; }

    public CameraState? Camera { get; init; }

    public bool BottomCollapsed { get; init; }

    /// <summary>0 = Tags, 1 = I/O-lijst, 2 = Trace, 3 = Cijfers.</summary>
    public int BottomTab { get; init; }

    public WindowState? Window { get; init; }

    /// <summary>Plan 30: show the welcome window at the next start (set after a first start, cleared by "Niet meer tonen").</summary>
    public bool ShowWelcome { get; init; }

    /// <summary>Recently opened or saved scenes, newest first, without doubles, at most <see cref="MaxRecent"/>.</summary>
    public IReadOnlyList<string> Recent { get; init; } = Array.Empty<string>();

    /// <summary>Puts a path in front of <see cref="Recent"/>, removes an earlier copy and cuts the list at ten.</summary>
    public AppSettings WithRecent(string path) => this with { Recent = CleanRecent(new[] { path }.Concat(Recent)) };

    /// <summary>True when both would be written as exactly the same pairs.</summary>
    public bool SameAs(AppSettings other)
    {
        IReadOnlyDictionary<string, string> mine = ToPairs();
        IReadOnlyDictionary<string, string> theirs = other.ToPairs();
        return mine.Count == theirs.Count
            && mine.All(pair => theirs.TryGetValue(pair.Key, out string? value) && value == pair.Value);
    }

    /// <summary>The settings as key/value pairs; numbers with a point, whatever the culture. Unset values are left out.</summary>
    public IReadOnlyDictionary<string, string> ToPairs()
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyHost] = Host,
            [KeyPort] = Text(Port),
            [KeyUnit] = Text(UnitId),
            [KeyCollapsed] = BottomCollapsed ? "true" : "false",
            [KeyTab] = Text(BottomTab),
        };

        if (ScenePath is not null)
        {
            pairs[KeyScene] = ScenePath;
        }

        if (Camera is { } camera)
        {
            if (CameraScene is not null)
            {
                pairs[KeyCameraScene] = CameraScene;
            }

            pairs[KeyCameraX] = Text(camera.PivotX);
            pairs[KeyCameraY] = Text(camera.PivotY);
            pairs[KeyCameraZ] = Text(camera.PivotZ);
            pairs[KeyCameraDistance] = Text(camera.Distance);
            pairs[KeyCameraYaw] = Text(camera.Yaw);
            pairs[KeyCameraPitch] = Text(camera.Pitch);
        }

        if (Window is { } window)
        {
            pairs[KeyWindowX] = Text(window.X);
            pairs[KeyWindowY] = Text(window.Y);
            pairs[KeyWindowWidth] = Text(window.Width);
            pairs[KeyWindowHeight] = Text(window.Height);
            pairs[KeyWindowMaximized] = window.Maximized ? "true" : "false";
        }

        for (int i = 0; i < Recent.Count; i++)
        {
            pairs[KeyRecent + Text(i)] = Recent[i];
        }

        if (ShowWelcome)
        {
            pairs[KeyWelcome] = "true";
        }

        return pairs;
    }

    /// <summary>Reads settings from key/value pairs. Unknown keys are ignored; a broken value falls back to its default.</summary>
    public static AppSettings FromPairs(IReadOnlyDictionary<string, string> pairs)
    {
        var defaults = new AppSettings();
        string? Get(string key) => pairs.TryGetValue(key, out string? value) ? value : null;

        int? port = Int(Get(KeyPort));
        int? unit = Int(Get(KeyUnit));
        int? tab = Int(Get(KeyTab));
        string? host = Get(KeyHost)?.Trim();
        string? scene = Get(KeyScene) ?? Get(LegacyKeyScene);

        var recent = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            if (Get(KeyRecent + Text(i)) is { } path)
            {
                recent.Add(ScenePaths.Upgrade(path));
            }
        }

        return new AppSettings
        {
            ScenePath = string.IsNullOrWhiteSpace(scene) ? null : ScenePaths.Upgrade(scene),
            Host = string.IsNullOrEmpty(host) ? defaults.Host : host,
            Port = port is >= 1 and <= 65535 ? port.Value : defaults.Port,
            UnitId = unit is >= 0 and <= 255 ? unit.Value : defaults.UnitId,
            CameraScene = (Get(KeyCameraScene) ?? Get(LegacyKeyCameraScene)) is { } cameraScene ? ScenePaths.Upgrade(cameraScene) : null,
            Camera = ReadCamera(Get),
            BottomCollapsed = Bool(Get(KeyCollapsed)) ?? defaults.BottomCollapsed,
            BottomTab = tab is >= 0 and < BottomTabCount ? tab.Value : defaults.BottomTab,
            Window = ReadWindow(Get),
            Recent = CleanRecent(recent),
            ShowWelcome = Bool(Get(KeyWelcome)) ?? defaults.ShowWelcome,
        };
    }

    private static CameraState? ReadCamera(Func<string, string?> get)
    {
        double? x = Double(get(KeyCameraX));
        double? y = Double(get(KeyCameraY));
        double? z = Double(get(KeyCameraZ));
        double? distance = Double(get(KeyCameraDistance));
        double? yaw = Double(get(KeyCameraYaw));
        double? pitch = Double(get(KeyCameraPitch));
        if (x is null || y is null || z is null || yaw is null
            || distance is not (>= MinCameraDistance and <= MaxCameraDistance)
            || pitch is not (>= MinCameraPitch and <= MaxCameraPitch))
        {
            return null;
        }

        return new CameraState(x.Value, y.Value, z.Value, distance.Value, yaw.Value, pitch.Value);
    }

    private static WindowState? ReadWindow(Func<string, string?> get)
    {
        int? x = Int(get(KeyWindowX));
        int? y = Int(get(KeyWindowY));
        int? width = Int(get(KeyWindowWidth));
        int? height = Int(get(KeyWindowHeight));
        if (x is null || y is null || width is not >= MinWindowWidth || height is not >= MinWindowHeight)
        {
            return null;
        }

        return new WindowState(x.Value, y.Value, width.Value, height.Value, Bool(get(KeyWindowMaximized)) ?? false);
    }

    private static IReadOnlyList<string> CleanRecent(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxRecent)
        .ToArray();

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static double? Double(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;

    private static bool? Bool(string? text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}
