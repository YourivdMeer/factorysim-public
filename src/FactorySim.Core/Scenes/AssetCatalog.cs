using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactorySim.Core.Scenes;

/// <summary>One asset of the library, as described in library/catalog.json.</summary>
/// <param name="Behaviour">Which simulator part drives it, or null when it is visual only.</param>
/// <param name="Internal">Used by other parts; not offered in the palette.</param>
/// <param name="File">Model path relative to the app's resource root.</param>
/// <param name="OriginHeight">Height of the model origin above the floor when placed.</param>
/// <param name="BoundsMin">Lower corner of the bounding box around the origin (x, y up, z).</param>
/// <param name="Parts">Names of the separate objects in the model, moving parts included.</param>
public sealed record CatalogAsset(
    string Id,
    string Name,
    string Category,
    string? Behaviour,
    bool Internal,
    string File,
    string Thumbnail,
    double OriginHeight,
    double[] BoundsMin,
    double[] BoundsMax,
    double[] Size,
    string[] Parts,
    int Triangles,
    Dictionary<string, double> Params)
{
    /// <summary>True when the simulator can run this asset; false when it is decoration.</summary>
    /// <summary>For an emitter: id of the library item it emits; null emits the two standard boxes.</summary>
    public string? Emits { get; init; }

    /// <summary>For a passive part: the boxes of its collider, in the part's own frame.</summary>
    public ColliderSpec[]? Colliders { get; init; }

    public bool IsFunctional => Behaviour is not null;

    /// <summary>True when it can be placed in a scene (items are spawned by emitters instead).</summary>
    public bool IsPlaceable => !Internal && Behaviour != AssetBehaviours.Item;

    public double Param(string name, double fallback) => Params.TryGetValue(name, out double value) ? value : fallback;
}

public sealed record CatalogCategory(string Id, string Name);

/// <summary>Values of <see cref="CatalogAsset.Behaviour"/> the simulator knows.</summary>
public static class AssetBehaviours
{
    public const string Conveyor = "conveyor";
    public const string Pusher = "pusher";
    public const string PhotoEye = "photo_eye";
    public const string Emitter = "emitter";
    public const string Remover = "remover";
    public const string Rail = "rail";

    /// <summary>A blade that rises through the transport surface and holds items back.</summary>
    public const string Stop = "stop";

    /// <summary>A conveyor section whose wheels can steer items off to the left or the right.</summary>
    public const string WheelSorter = "wheel_sorter";

    /// <summary>A gantry with a gripper that lifts an item and sets it down somewhere else.</summary>
    public const string PickPlace = "pick_place";

    /// <summary>A roller platform that travels between a lower and an upper scene.</summary>
    public const string Elevator = "elevator";

    /// <summary>Palletizer: forms layers of boxes on a plate and drops them onto a pallet on a lifting table.</summary>
    public const string Palletizer = "palletizer";

    /// <summary>Machining center: a robot loads a part into a machine that turns raw material into a base or a lid.</summary>
    public const string Machining = "machining";

    /// <summary>Stacker crane: drives along an aisle, hoists a platform and reaches into a rack with forks.</summary>
    public const string StackerCrane = "stacker_crane";

    /// <summary>A sensor with a reflector across the line: reports an item that interrupts the beam. Works against <see cref="Reflector"/>.</summary>
    public const string Retroreflective = "retroreflective";

    /// <summary>The reflector of a reflection sensor: no tags.</summary>
    public const string Reflector = "reflector";

    /// <summary>The receiver of a light array: one tag, on while any beam is interrupted. Works against <see cref="LightEmitter"/>.</summary>
    public const string LightArray = "light_array";

    /// <summary>The emitter of a light array: no tags.</summary>
    public const string LightEmitter = "light_emitter";

    /// <summary>A wheel on a belt that gives two tracks of pulses (A and B) while the belt moves.</summary>
    public const string Encoder = "encoder";

    /// <summary>A part that is only in the way, with colliders from the catalogue and no tags: chute, free rollers, guide, corner stop.</summary>
    public const string Passive = "passive";

    /// <summary>Two clamps either side of a conveyor that close on an item and put it in the middle.</summary>
    public const string Clamp = "clamp";

    /// <summary>A storage rack for pallets: supports only, no tags.</summary>
    public const string Rack = "rack";

    /// <summary>A roller deck that turns a quarter turn about the vertical axis.</summary>
    public const string Turntable = "turntable";

    /// <summary>An arm beside a conveyor that swings across it and guides items off the other side.</summary>
    public const string ArmSorter = "arm_sorter";

    /// <summary>A control the operator clicks: push button, emergency stop, selector. One sensor tag.</summary>
    public const string Button = "button";

    /// <summary>One or more lamps the PLC switches: indicator, warning light, stack light.</summary>
    public const string Lamp = "lamp";

    /// <summary>A six-axis robot arm on a pedestal that moves between taught positions and holds an item by suction.</summary>
    public const string Robot = "robot";

    /// <summary>A 90-degree conveyor bend around the asset origin.</summary>
    public const string Curve = "curve";

    /// <summary>A knob the operator turns: one Int sensor tag, 0 to 1000.</summary>
    public const string Potentiometer = "potentiometer";

    /// <summary>A digital display that shows the number of one Int actuator tag.</summary>
    public const string Display = "display";

    /// <summary>A tank with a fill and a discharge valve (Int actuators) and a scene and a flow (Int sensors).</summary>
    public const string Tank = "tank";

    /// <summary>A camera that reports the kind of item in its view as a number, and whether it sees one.</summary>
    public const string Vision = "vision";

    /// <summary>A siren the PLC switches: flashes and sounds a tone while its tag is on.</summary>
    public const string Siren = "siren";

    /// <summary>A text in the scene: on the floor or on a sign on a post. No tags.</summary>
    public const string Sign = "sign";
    public const string Item = "item";
}

public sealed class CatalogException : Exception
{
    public CatalogException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The asset library: what exists, in which category, and how each asset behaves.</summary>
public sealed class AssetCatalog
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, CatalogAsset> _byId;

    public AssetCatalog(IEnumerable<CatalogCategory> categories, IEnumerable<CatalogAsset> assets)
    {
        Categories = categories.ToArray();
        Assets = assets.ToArray();
        _byId = new Dictionary<string, CatalogAsset>(StringComparer.Ordinal);
        foreach (CatalogAsset asset in Assets)
        {
            if (!_byId.TryAdd(asset.Id, asset))
            {
                throw new CatalogException($"Asset '{asset.Id}' is listed more than once.");
            }
        }
    }

    public IReadOnlyList<CatalogCategory> Categories { get; }

    public IReadOnlyList<CatalogAsset> Assets { get; }

    public static AssetCatalog FromJson(string json)
    {
        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new CatalogException($"Catalogue is not valid JSON: {e.Message}", e);
        }

        if (file is null || file.Format != "factorysim-catalog" || file.Assets is null)
        {
            throw new CatalogException("Not a FactorySim asset catalogue.");
        }

        return new AssetCatalog(file.Categories ?? Array.Empty<CatalogCategory>(), file.Assets);
    }

    public CatalogAsset? Find(string id) => _byId.GetValueOrDefault(id);

    public CatalogAsset Get(string id) =>
        Find(id) ?? throw new CatalogException($"The library has no asset '{id}'.");

    /// <summary>Assets of one category that can be placed in a scene, in catalogue order.</summary>
    public IEnumerable<CatalogAsset> Placeable(string categoryId) =>
        Assets.Where(asset => asset.Category == categoryId && asset.IsPlaceable);

    /// <summary>
    /// Placeable assets whose name, id or category name contains the text; all of them for an empty text.
    /// </summary>
    public IReadOnlyList<CatalogAsset> Search(string? text, string? categoryId = null)
    {
        string needle = text?.Trim() ?? string.Empty;
        return Assets.Where(asset => asset.IsPlaceable
            && (categoryId is null || asset.Category == categoryId)
            && (needle.Length == 0
                || asset.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || asset.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || CategoryName(asset.Category).Contains(needle, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private string CategoryName(string categoryId) =>
        Categories.FirstOrDefault(category => category.Id == categoryId)?.Name ?? string.Empty;

    private sealed record CatalogFile(string? Format, int Version, CatalogCategory[]? Categories, CatalogAsset[]? Assets);
}
