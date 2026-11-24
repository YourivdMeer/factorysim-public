using System.Globalization;
using System.Text.Json;
using FactorySim.Core.Robot;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Scenes;

/// <summary>One placed asset in a scene.</summary>
/// <param name="Id">Stable identifier within the scene.</param>
/// <param name="Asset">Id of the asset in the library.</param>
/// <param name="Name">Name shown to the user; tags of the item are derived from it.</param>
/// <param name="Y">Height of the model origin above the floor.</param>
/// <param name="Yaw">Rotation about the vertical axis, in degrees.</param>
/// <param name="Params">Per-item overrides of the asset's parameters (length, range, alwaysOn, ...).</param>
/// <param name="Emits">For an emitter: id of the library item it puts on the line instead of what the catalogue says; null keeps the default.</param>
/// <param name="Poses">For a robot arm: the positions taught in the editor, sorted by number; null for none.</param>
/// <param name="Text">For a sign: the text it shows (see <see cref="SignText"/>); null shows the item's name.</param>
public sealed record SceneItem(
    string Id,
    string Asset,
    string Name,
    double X,
    double Y,
    double Z,
    double Yaw,
    Dictionary<string, double>? Params = null,
    string? Emits = null,
    IReadOnlyList<RobotPose>? Poses = null,
    string? Text = null)
{
    public double Param(string name, CatalogAsset asset, double fallback) =>
        Params is not null && Params.TryGetValue(name, out double value) ? value : asset.Param(name, fallback);
}

/// <summary>The size of the floor of a scene, in metres: <see cref="Length"/> along X and <see cref="Width"/> along Z, centred on the origin.</summary>
public readonly record struct FloorSize(double Length, double Width);

public sealed class SceneException : Exception
{
    public SceneException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// A scene (project): the placed assets plus the tag mapping to the PLC.
/// Saved, exported and imported as one JSON file.
/// </summary>
public sealed class Scene
{
    public const string Format = "factorysim-scene";

    /// <summary>The format name of files from before plan 22; still read, never written.</summary>
    public const string LegacyFormat = "factorysim-level";
    public const int Version = 1;

    /// <summary>Grid the editor snaps to, in metres.</summary>
    public const double GridStep = 0.25;

    /// <summary>The floor of a scene without a floor size of its own.</summary>
    public static readonly FloorSize DefaultFloor = new(14.0, 10.0);

    /// <summary>Smallest and largest side of a floor, in metres.</summary>
    public const double MinFloor = 4.0;

    public const double MaxFloor = 60.0;

    /// <summary>Floor sizes are rounded to this step, in metres.</summary>
    public const double FloorStep = 0.5;

    public string Name { get; set; } = "Nieuwe scène";

    /// <summary>Longest description of a scene, in characters.</summary>
    public const int MaxDescription = 400;

    /// <summary>What the scene is for, shown in the scene gallery (plan 20); empty when there is none.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Sets the description: trimmed, plain line breaks, cut at <see cref="MaxDescription"/> characters; null clears it.</summary>
    public void SetDescription(string? text)
    {
        string clean = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        Description = clean.Length > MaxDescription ? clean[..MaxDescription].TrimEnd() : clean;
    }

    public List<SceneItem> Items { get; } = new();

    /// <summary>Size of the floor; the default unless the scene says otherwise.</summary>
    public FloorSize Floor { get; private set; } = DefaultFloor;

    /// <summary>Sets the size of the floor, rounded to half a metre. Throws when it is not a sensible size.</summary>
    public void SetFloor(double length, double width)
    {
        if (!(length >= MinFloor && length <= MaxFloor && width >= MinFloor && width <= MaxFloor))
        {
            throw new SceneException($"De vloer moet tussen {MinFloor:0} en {MaxFloor:0} meter lang en breed zijn.");
        }

        Floor = new FloorSize(RoundFloor(length), RoundFloor(width));
    }

    /// <summary>True when the floor point (x, z) lies on the floor.</summary>
    public bool OnFloor(double x, double z) => Math.Abs(x) <= Floor.Length / 2.0 && Math.Abs(z) <= Floor.Width / 2.0;

    private static double RoundFloor(double value) => Math.Round(value / FloorStep, MidpointRounding.AwayFromZero) * FloorStep;

    /// <summary>The optional simulation tags (plan 14) this scene switches on, in the order of <see cref="SceneTags.OptionalSimTags"/>; empty by default.</summary>
    public IReadOnlyList<string> SimTags { get; private set; } = Array.Empty<string>();

    /// <summary>Switches on exactly the given optional simulation tags. Throws for a name that is not one of them.</summary>
    public void SetSimTags(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!SceneTags.OptionalSimTags.Contains(name))
            {
                throw new SceneException(
                    $"'{name}' is not a simulation tag that can be switched on; use {string.Join(" or ", SceneTags.OptionalSimTags.Select(n => $"'{n}'"))}.");
            }

            wanted.Add(name);
        }

        SimTags = SceneTags.OptionalSimTags.Where(wanted.Contains).ToArray();
    }

    /// <summary>Tag mapping as last saved; <see cref="SceneTags.Build"/> keeps these offsets stable.</summary>
    public List<TagMapEntry> Tags { get; } = new();

    public static double Snap(double value, double step = GridStep) => Math.Round(value / step) * step;

    public SceneItem? Find(string id) => Items.FirstOrDefault(item => item.Id == id);

    /// <summary>Places an asset on the floor at (x, z), snapped to the grid, and returns the new item.</summary>
    public SceneItem Add(CatalogAsset asset, double x, double z, double yaw = 0.0)
    {
        if (!asset.IsPlaceable)
        {
            throw new SceneException($"Asset '{asset.Id}' cannot be placed in a scene.");
        }

        var item = new SceneItem(NewId(asset.Id), asset.Id, UniqueName(DefaultName(asset)), Snap(x), asset.OriginHeight,
            Snap(z), NormalizeYaw(yaw));
        Items.Add(item);
        return item;
    }

    public bool Remove(string id) => Items.RemoveAll(item => item.Id == id) > 0;

    // ---- groups: one operation on several items at once

    /// <summary>The middle of the extent of the given items on the floor, on the grid. (0, 0) for none.</summary>
    public (double X, double Z) Centre(IEnumerable<string> ids) => CentreOf(Members(ids));

    /// <summary>
    /// Shifts the given items over the floor by exactly the same amount (to the millimetre), so items that were
    /// clicked together off the grid (plan 17) stay together; ids that are not in the scene are ignored.
    /// </summary>
    public void MoveMany(IEnumerable<string> ids, double dx, double dz)
    {
        foreach (SceneItem item in Members(ids))
        {
            Place(item.Id, item.X + dx, item.Z + dz, item.Yaw);
        }
    }

    /// <summary>
    /// Turns the given items about the point (cx, cz) and each of them about itself by the same angle, in
    /// degrees; a positive angle takes +X towards -Z, like the yaw of an item.
    /// </summary>
    public void RotateMany(IEnumerable<string> ids, double degrees, double cx, double cz)
    {
        double angle = degrees * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        foreach (SceneItem item in Members(ids))
        {
            double rx = item.X - cx;
            double rz = item.Z - cz;
            double x = Exact(cx + rx * cos + rz * sin);      // exact: a clicked-together line stays together (plan 17)
            double z = Exact(cz - rx * sin + rz * cos);
            Replace(item.Id, current => current with { X = x, Z = z, Yaw = NormalizeYaw(current.Yaw + degrees) });
        }
    }

    /// <summary>Adds a copy of every given item, shifted over the floor, each under a name of its own; returns the copies.</summary>
    public IReadOnlyList<SceneItem> DuplicateMany(IEnumerable<string> ids, double dx, double dz) =>
        Members(ids).ToArray().Select(source => PasteExact(source, source.X + dx, source.Z + dz)).ToArray();

    /// <summary>Adds copies of items (which need not be in the scene any more) so that the middle of the group lands at (x, z).</summary>
    public IReadOnlyList<SceneItem> PasteMany(IReadOnlyList<SceneItem> sources, double x, double z)
    {
        (double cx, double cz) = CentreOf(sources);
        double dx = Snap(x) - cx;
        double dz = Snap(z) - cz;
        return sources.Select(source => PasteExact(source, source.X + dx, source.Z + dz)).ToArray();
    }

    /// <summary>Removes the given items; returns how many were in the scene.</summary>
    public int RemoveMany(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        return Items.RemoveAll(item => set.Contains(item.Id));
    }

    private IEnumerable<SceneItem> Members(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        return Items.Where(item => set.Contains(item.Id)).ToArray();
    }

    private static (double X, double Z) CentreOf(IEnumerable<SceneItem> items)
    {
        SceneItem[] list = items.ToArray();
        if (list.Length == 0)
        {
            return (0.0, 0.0);
        }

        return (Snap((list.Min(item => item.X) + list.Max(item => item.X)) / 2.0),
            Snap((list.Min(item => item.Z) + list.Max(item => item.Z)) / 2.0));
    }

    public SceneItem Move(string id, double x, double z) => Replace(id, item => item with { X = Snap(x), Z = Snap(z) });

    /// <summary>
    /// Puts an item at an exact place and angle, rounded to the millimetre instead of the grid. Clicking belts
    /// onto each other (plan 17) needs this: a 1.6 m belt after a 4 m one does not end on the grid.
    /// </summary>
    public SceneItem Place(string id, double x, double z, double yaw) =>
        Replace(id, item => item with { X = Exact(x), Z = Exact(z), Yaw = NormalizeYaw(yaw) });

    /// <summary>
    /// Turns the given items about (pivotX, pivotZ) and each about itself by <paramref name="degrees"/>, then shifts
    /// them by (dx, dz); exact to the millimetre. A positive angle takes +X towards -Z, like the yaw of an item.
    /// </summary>
    public void TransformMany(IEnumerable<string> ids, double pivotX, double pivotZ, double degrees, double dx, double dz)
    {
        double angle = degrees * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        foreach (SceneItem item in Members(ids))
        {
            double rx = item.X - pivotX;
            double rz = item.Z - pivotZ;
            Place(item.Id, pivotX + rx * cos + rz * sin + dx, pivotZ - rx * sin + rz * cos + dz, item.Yaw + degrees);
        }
    }

    private static double Exact(double value) => Math.Round(value, 3) + 0.0;    // + 0.0 turns -0 into 0

    /// <summary>Turns an item a quarter turn (or any other step) about the vertical axis.</summary>
    public SceneItem Rotate(string id, double degrees = 90.0) =>
        Replace(id, item => item with { Yaw = NormalizeYaw(item.Yaw + degrees) });

    public SceneItem Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new SceneException("A name cannot be empty.");
        }

        if (Items.Any(item => item.Id != id && item.Name == name))
        {
            throw new SceneException($"The name '{name}' is already in use.");
        }

        return Replace(id, item => item with { Name = name });
    }

    /// <summary>Adds a copy of an item, shifted over the floor, under a new id and name.</summary>
    public SceneItem Duplicate(string id, double dx, double dz)
    {
        SceneItem source = Find(id) ?? throw new SceneException($"There is no item '{id}'.");
        return PasteExact(source, source.X + dx, source.Z + dz);
    }

    /// <summary>Adds a copy of an item (which need not be in the scene any more) at a floor position on the grid.</summary>
    public SceneItem Paste(SceneItem source, double x, double z) => PasteExact(source, Snap(x), Snap(z));

    /// <summary>Adds a copy of an item at an exact floor position (to the millimetre), under a new id and name.</summary>
    private SceneItem PasteExact(SceneItem source, double x, double z)
    {
        string baseName = source.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimEnd();
        var copy = source with
        {
            Id = NewId(source.Asset),
            Name = UniqueName(baseName.Length == 0 ? source.Name : baseName),
            X = Exact(x),
            Z = Exact(z),
            Params = source.Params is null ? null : new Dictionary<string, double>(source.Params),
            Poses = source.Poses?.ToArray(),
        };
        Items.Add(copy);
        return copy;
    }

    /// <summary>Sets the item an emitter puts on the line; null or empty goes back to the default of its asset.</summary>
    public SceneItem SetEmits(string id, string? itemId) => Replace(id, item => item with { Emits = string.IsNullOrEmpty(itemId) ? null : itemId });

    /// <summary>Sets the text of a sign; it is cut to <see cref="SignText.MaxLength"/> characters and three lines, and empty clears it.</summary>
    public SceneItem SetText(string id, string? text) => Replace(id, item => item with { Text = SignText.Normalize(text) });

    /// <summary>Sets the taught positions of a robot; an empty list clears them.</summary>
    public SceneItem SetPoses(string id, IReadOnlyList<RobotPose>? poses) => Replace(id, item => item with
    {
        Poses = poses is { Count: > 0 } ? RobotPoses.Validate(poses, id) : null,
    });

    public SceneItem SetParam(string id, string name, double value) => Replace(id, item =>
    {
        var parameters = item.Params is null ? new Dictionary<string, double>() : new Dictionary<string, double>(item.Params);
        parameters[name] = value;
        return item with { Params = parameters };
    });

    /// <summary>A name not used by another item: "Conveyor", "Conveyor 2", "Conveyor 3", ...</summary>
    public string UniqueName(string baseName)
    {
        if (Items.All(item => item.Name != baseName))
        {
            return baseName;
        }

        for (int number = 2; ; number++)
        {
            string candidate = $"{baseName} {number.ToString(CultureInfo.InvariantCulture)}";
            if (Items.All(item => item.Name != candidate))
            {
                return candidate;
            }
        }
    }

    public string ToJson() => SceneSerializer.ToJson(this);

    public static Scene FromJson(string json) => SceneSerializer.FromJson(json);

    private static string DefaultName(CatalogAsset asset) => asset.Behaviour switch
    {
        AssetBehaviours.Conveyor => "Conveyor",
        AssetBehaviours.Pusher => "Pusher",
        AssetBehaviours.PhotoEye when asset.Id.StartsWith("capacitive", StringComparison.Ordinal) => "Capacitive sensor",
        AssetBehaviours.PhotoEye when asset.Id.StartsWith("inductive", StringComparison.Ordinal) => "Inductive sensor",
        AssetBehaviours.PhotoEye => "Sensor",
        AssetBehaviours.Retroreflective => "Retroreflective sensor",
        AssetBehaviours.Reflector => "Reflector",
        AssetBehaviours.LightArray => "Light array",
        AssetBehaviours.LightEmitter => "Light array emitter",
        AssetBehaviours.Encoder => "Encoder",
        AssetBehaviours.Emitter => "Emitter",
        AssetBehaviours.Remover => "Remover",
        AssetBehaviours.Rail => "Rail",
        AssetBehaviours.Stop => "Stop",
        AssetBehaviours.Curve => "Curve",
        AssetBehaviours.WheelSorter when asset.Id.StartsWith("chain", StringComparison.Ordinal) => "Chain transfer",
        AssetBehaviours.WheelSorter => "Wheel sorter",
        AssetBehaviours.Turntable => "Turntable",
        AssetBehaviours.Elevator => "Elevator",
        AssetBehaviours.Palletizer => "Palletizer",
        AssetBehaviours.Machining => "Machining center",
        AssetBehaviours.StackerCrane => "Stacker crane",
        AssetBehaviours.Rack => "Rack",
        AssetBehaviours.Clamp => "Clamp",
        AssetBehaviours.PickPlace => "Pick and place",
        AssetBehaviours.ArmSorter => "Arm sorter",
        AssetBehaviours.Robot => "Robot",
        AssetBehaviours.Potentiometer => "Potentiometer",
        AssetBehaviours.Display => "Digital display",
        AssetBehaviours.Tank => "Tank",
        AssetBehaviours.Vision => "Vision sensor",
        AssetBehaviours.Siren => "Siren",
        AssetBehaviours.Button when asset.Id.StartsWith("emergency", StringComparison.Ordinal) => "Emergency stop",
        AssetBehaviours.Button when asset.Id.StartsWith("safety", StringComparison.Ordinal) => "Safety door",
        AssetBehaviours.Button when asset.Id.StartsWith("selector", StringComparison.Ordinal) => "Selector",
        AssetBehaviours.Button => "Button",
        AssetBehaviours.Lamp when asset.Id.StartsWith("stack", StringComparison.Ordinal) => "Stack light",
        AssetBehaviours.Lamp when asset.Id.StartsWith("warning", StringComparison.Ordinal) => "Warning light",
        AssetBehaviours.Lamp => "Lamp",
        _ => asset.Name,
    };

    private static double NormalizeYaw(double degrees)
    {
        double yaw = degrees % 360.0;
        return yaw < 0.0 ? yaw + 360.0 : yaw;
    }

    private string NewId(string assetId)
    {
        for (int number = 1; ; number++)
        {
            string candidate = $"{assetId}-{number.ToString(CultureInfo.InvariantCulture)}";
            if (Items.All(item => item.Id != candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Puts an item's origin at another height above the floor, in metres.</summary>
    public SceneItem SetHeight(string id, double y)
    {
        if (!(y >= 0.0) || double.IsInfinity(y))
        {
            throw new SceneException("De hoogte moet 0 of meer zijn.");
        }

        return Replace(id, item => item with { Y = y });
    }

    private SceneItem Replace(string id, Func<SceneItem, SceneItem> change)
    {
        int index = Items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            throw new SceneException($"The scene has no item '{id}'.");
        }

        Items[index] = change(Items[index]);
        return Items[index];
    }
}

/// <summary>Reads and writes the scene file format.</summary>
public static class SceneSerializer
{
    public static string ToJson(Scene scene)
    {
        var file = new SceneFile(
            Scene.Format,
            Scene.Version,
            scene.Name,
            scene.Items.Select(ToDto).ToArray(),
            scene.Tags.ToArray(),
            scene.Floor == Scene.DefaultFloor ? null : new[] { scene.Floor.Length, scene.Floor.Width },
            scene.SimTags.Count == 0 ? null : scene.SimTags.ToArray(),
            scene.Description.Length == 0 ? null : scene.Description);
        return JsonSerializer.Serialize(file, AssetCatalog.JsonOptions) + "\n";
    }

    public static Scene FromJson(string json)
    {
        SceneFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SceneFile>(json, AssetCatalog.JsonOptions);
        }
        catch (JsonException e)
        {
            throw new SceneException($"The file is not valid JSON: {e.Message}", e);
        }

        if (file is null || (file.Format != Scene.Format && file.Format != Scene.LegacyFormat))
        {
            throw new SceneException("This is not a FactorySim scene file.");
        }

        if (file.Version > Scene.Version)
        {
            throw new SceneException(
                $"This scene was saved by a newer version (format {file.Version}); this app reads up to {Scene.Version}.");
        }

        var scene = new Scene { Name = string.IsNullOrWhiteSpace(file.Name) ? "Naamloos" : file.Name! };
        scene.SetDescription(file.Description);
        scene.Items.AddRange(FromDtos(file.Items));

        scene.Tags.AddRange(file.Tags ?? Array.Empty<TagMapEntry>());
        if (file.Floor is { Length: 2 })
        {
            scene.SetFloor(file.Floor[0], file.Floor[1]);
        }

        if (file.SimTags is not null)
        {
            scene.SetSimTags(file.SimTags);
        }

        return scene;
    }

    /// <summary>
    /// The file form of an item. Shared with building blocks (plan 18), so a new item field ends up in both formats.
    /// </summary>
    internal static ItemDto ToDto(SceneItem item) =>
        new(item.Id, item.Asset, item.Name, new[] { item.X, item.Y, item.Z }, item.Yaw,
            item.Params is { Count: > 0 } ? item.Params : null, item.Emits,
            item.Poses?.Select(pose => new PoseDto(pose.Number, pose.Name, pose.Angles.ToArray())).ToArray(), item.Text);

    /// <summary>Reads the items of a file; throws a <see cref="SceneException"/> for a missing id, asset or position, or a repeated id.</summary>
    internal static IEnumerable<SceneItem> FromDtos(IEnumerable<ItemDto>? items)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SceneItem>();
        foreach (ItemDto item in items ?? Array.Empty<ItemDto>())
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Asset))
            {
                throw new SceneException("An item in the file has no id or no asset.");
            }

            if (!ids.Add(item.Id))
            {
                throw new SceneException($"Item id '{item.Id}' occurs more than once.");
            }

            if (item.Position is not { Length: 3 })
            {
                throw new SceneException($"Item '{item.Id}' has no valid position.");
            }

            result.Add(new SceneItem(item.Id, item.Asset, item.Name ?? item.Id, item.Position[0], item.Position[1],
                item.Position[2], item.Yaw, item.Params, string.IsNullOrEmpty(item.Emits) ? null : item.Emits, ReadPoses(item),
                SignText.Normalize(item.Text)));
        }

        return result;
    }

    private static IReadOnlyList<RobotPose>? ReadPoses(ItemDto item)
    {
        if (item.Poses is not { Length: > 0 })
        {
            return null;
        }

        var poses = new List<RobotPose>();
        foreach (PoseDto pose in item.Poses)
        {
            if (pose.Angles is not { Length: RobotAngles.Count })
            {
                throw new SceneException($"Robot '{item.Id}' has a position without six angles.");
            }

            poses.Add(new RobotPose(pose.Number, string.IsNullOrWhiteSpace(pose.Name) ? $"Positie {pose.Number}" : pose.Name!,
                RobotAngles.FromArray(pose.Angles)));
        }

        return RobotPoses.Validate(poses, item.Id);
    }

    internal sealed record ItemDto(string Id, string Asset, string? Name, double[]? Position, double Yaw,
        Dictionary<string, double>? Params, string? Emits = null, PoseDto[]? Poses = null, string? Text = null);

    internal sealed record PoseDto(int Number, string? Name, double[]? Angles);

    private sealed record SceneFile(string? Format, int Version, string? Name, ItemDto[]? Items, TagMapEntry[]? Tags,
        double[]? Floor = null, string[]? SimTags = null, string? Description = null);
}
