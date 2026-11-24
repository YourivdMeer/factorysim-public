using System.Text.Json;

namespace FactorySim.Core.Scenes;

/// <summary>The colour group of an item on the floor plan of a scene card.</summary>
public enum FootprintKind
{
    Transport,
    Sensor,
    Station,
    Other,
}

/// <summary>A point on the floor: x and z in metres.</summary>
public readonly record struct FloorPoint(double X, double Z);

/// <summary>The outline of one item on the floor, turned with its yaw, as four corners in floor coordinates.</summary>
public sealed record ItemFootprint(IReadOnlyList<FloorPoint> Corners, FootprintKind Kind);

/// <summary>
/// What the scene gallery (plan 20) shows of one scene file: name, description, counts and a floor plan.
/// A file that cannot be read gives a summary with an <see cref="Error"/> instead of an exception.
/// </summary>
public sealed record SceneSummary(
    string Name,
    string Description,
    int ItemCount,
    int TagCount,
    IReadOnlyList<ItemFootprint> Footprints,
    FloorSize Floor,
    DateTime? FileTime,
    IReadOnlyList<string> UnknownAssets,
    string? Error = null)
{
    /// <summary>The first two lines of the description.</summary>
    public string DescriptionPreview => string.Join("\n", Description.Split('\n').Take(2));

    /// <summary>A note about assets the library does not have, or null.</summary>
    public string? Warning => UnknownAssets.Count == 0 ? null : $"Onbekende onderdelen, niet getekend: {string.Join(", ", UnknownAssets)}";

    public static SceneSummary From(Scene scene, AssetCatalog catalog, DateTime? fileTime)
    {
        var footprints = new List<ItemFootprint>();
        var unknown = new List<string>();
        foreach (SceneItem item in scene.Items)
        {
            if (catalog.Find(item.Asset) is not { } asset)
            {
                if (!unknown.Contains(item.Asset))
                {
                    unknown.Add(item.Asset);
                }

                continue;
            }

            footprints.Add(new ItemFootprint(Corners(item, asset), KindOf(asset)));
        }

        int tags;
        try
        {
            tags = SceneTags.Build(scene, catalog).Count;
        }
        catch (Exception e) when (e is SceneException or InvalidOperationException)
        {
            tags = scene.Tags.Count;
        }

        return new SceneSummary(scene.Name, scene.Description, footprints.Count, tags, footprints, scene.Floor, fileTime, unknown);
    }

    /// <summary>Reads a scene file's text; a file that is not a scene gives a summary with an error, named after the file.</summary>
    public static SceneSummary FromJson(string? json, AssetCatalog catalog, DateTime? fileTime, string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Broken(fileName, fileTime, "Het bestand is leeg.");
            }

            return From(Scene.FromJson(json), catalog, fileTime);
        }
        catch (Exception e) when (e is SceneException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Broken(fileName, fileTime, e.Message);
        }
    }

    /// <summary>The summaries whose name or description contains the text, ignoring case; all of them for an empty text.</summary>
    public static IReadOnlyList<SceneSummary> Search(IEnumerable<SceneSummary> summaries, string? text)
    {
        string wanted = (text ?? string.Empty).Trim();
        return wanted.Length == 0
            ? summaries.ToArray()
            : summaries.Where(summary => summary.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
                || summary.Description.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    /// <summary>Which colour an asset gets on the floor plan.</summary>
    public static FootprintKind KindOf(CatalogAsset asset) => asset.Category switch
    {
        "light_load" or "heavy_load" => FootprintKind.Transport,
        "sensors" => FootprintKind.Sensor,
        "stations" => FootprintKind.Station,
        _ => FootprintKind.Other,
    };

    private static SceneSummary Broken(string fileName, DateTime? fileTime, string error) =>
        new(fileName, string.Empty, 0, 0, Array.Empty<ItemFootprint>(), Scene.DefaultFloor, fileTime, Array.Empty<string>(), error);

    private static FloorPoint[] Corners(SceneItem item, CatalogAsset asset)
    {
        (double minX, double maxX, double minZ, double maxZ) = ScenePicking.Footprint(item, asset);
        double yaw = item.Yaw * Math.PI / 180.0;
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);

        // A positive yaw turns local +X towards -Z (as in ScenePicking.ItemsIn).
        FloorPoint Place(double x, double z) => new(item.X + x * cos + z * sin, item.Z - x * sin + z * cos);
        return new[] { Place(minX, minZ), Place(maxX, minZ), Place(maxX, maxZ), Place(minX, maxZ) };
    }
}
