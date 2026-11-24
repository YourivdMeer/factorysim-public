using System.Text;
using System.Text.Json;

namespace FactorySim.Core.Scenes;

/// <summary>
/// Building blocks (plan 18): saving a selection as a <c>.fsblock.json</c> and placing it again. The file uses the
/// item form of the scene file, so every item field (params, emits, robot positions, ...) goes along.
/// </summary>
public static class SceneBlocks
{
    public const string Format = "factorysim-block";
    public const int Version = 1;
    public const string Extension = ".fsblock.json";

    /// <summary>The given items of a scene as a block, positioned relative to the middle of the group.</summary>
    public static SceneBlock FromSelection(Scene scene, IEnumerable<string> ids, string name, string description)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new SceneException("Een bouwblok heeft een naam nodig.");
        }

        var set = ids.ToHashSet(StringComparer.Ordinal);
        SceneItem[] members = scene.Items.Where(item => set.Contains(item.Id)).ToArray();
        if (members.Length == 0)
        {
            throw new SceneException("Een bouwblok heeft minstens één onderdeel nodig.");
        }

        (double cx, double cz) = scene.Centre(set);
        SceneItem[] items = members.Select(item => item with
        {
            X = Exact(item.X - cx),
            Z = Exact(item.Z - cz),
            Params = item.Params is null ? null : new Dictionary<string, double>(item.Params),
            Poses = item.Poses?.ToArray(),
        }).ToArray();
        return new SceneBlock(name, description.Trim(), items);
    }

    /// <summary>
    /// Adds the items of a block to a scene with the middle of the group at (x, z) on the grid, the whole group turned
    /// by <paramref name="yaw"/> degrees about its middle (a positive angle takes +X towards -Z). Every item gets a new
    /// id and a name of its own, like pasting. Items whose asset is not in the library are skipped.
    /// </summary>
    public static BlockPlacement Place(Scene scene, SceneBlock block, AssetCatalog catalog, double x, double z, double yaw)
    {
        double angle = yaw * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        var skipped = new List<string>();
        var sources = new List<SceneItem>();
        foreach (SceneItem item in block.Items)
        {
            if (catalog.Find(item.Asset) is null)
            {
                skipped.Add(item.Name);
                continue;
            }

            sources.Add(item with
            {
                X = Exact(item.X * cos + item.Z * sin),
                Z = Exact(-item.X * sin + item.Z * cos),
                Yaw = NormalizeYaw(item.Yaw + yaw),
            });
        }

        IReadOnlyList<SceneItem> placed = sources.Count == 0 ? Array.Empty<SceneItem>() : scene.PasteMany(sources, x, z);
        return new BlockPlacement(placed, skipped);
    }

    public static string ToJson(SceneBlock block)
    {
        var file = new BlockFile(Format, Version, block.Name, block.Description,
            block.Items.Select(SceneSerializer.ToDto).ToArray());
        return JsonSerializer.Serialize(file, AssetCatalog.JsonOptions) + "\n";
    }

    public static SceneBlock FromJson(string json)
    {
        BlockFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BlockFile>(json, AssetCatalog.JsonOptions);
        }
        catch (JsonException e)
        {
            throw new SceneException($"The file is not valid JSON: {e.Message}", e);
        }

        if (file is null || file.Format != Format)
        {
            throw new SceneException("This is not a FactorySim building block.");
        }

        if (file.Version > Version)
        {
            throw new SceneException(
                $"This building block was saved by a newer version (format {file.Version}); this app reads up to {Version}.");
        }

        return new SceneBlock(string.IsNullOrWhiteSpace(file.Name) ? "Naamloos" : file.Name!.Trim(),
            file.Description?.Trim() ?? string.Empty, SceneSerializer.FromDtos(file.Items).ToArray());
    }

    /// <summary>A file name for a block: its name in lower case, other characters as one underscore, plus the extension.</summary>
    public static string FileName(string name)
    {
        var builder = new StringBuilder();
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        string stem = builder.ToString().Trim('_');
        return (stem.Length == 0 ? "blok" : stem) + Extension;
    }

    private static double Exact(double value) => Math.Round(value, 3) + 0.0;

    private static double NormalizeYaw(double yaw)
    {
        double result = Math.Round(yaw % 360.0, 6);
        return result < 0.0 ? result + 360.0 : result + 0.0;
    }

    private sealed record BlockFile(string? Format, int Version, string? Name, string? Description,
        SceneSerializer.ItemDto[]? Items);
}
