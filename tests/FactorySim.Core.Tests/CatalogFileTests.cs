using System.Text.Json;

namespace FactorySim.Core.Tests;

/// <summary>
/// The two copies of library/catalog.json the generator writes: the one in assets/ and the one the app
/// imports. Plan 37: an asset carries only the keys the app reads, nothing kept for an inventory.
/// </summary>
public class CatalogFileTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string[] KnownKeys =
    {
        "id", "name", "category", "behaviour", "internal", "file", "thumbnail", "originHeight",
        "boundsMin", "boundsMax", "size", "parts", "triangles", "params", "emits", "colliders",
    };

    private static string AssetsCopy => Path.Combine(RepoRoot, "assets", "library", "catalog.json");

    private static string AppCopy => Path.Combine(RepoRoot, "src", "FactorySim.App", "library", "catalog.json");

    [Fact]
    public void Both_copies_of_the_catalogue_are_the_same()
    {
        string Normalized(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        Assert.Equal(Normalized(AssetsCopy), Normalized(AppCopy));
    }

    [Fact]
    public void Every_asset_has_only_keys_the_app_reads()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(AppCopy));
        var unknown = document.RootElement.GetProperty("assets").EnumerateArray()
            .SelectMany(asset => asset.EnumerateObject()
                .Where(property => !KnownKeys.Contains(property.Name))
                .Select(property => $"{asset.GetProperty("id").GetString()}.{property.Name}"))
            .ToList();

        Assert.True(unknown.Count == 0, "unknown keys: " + string.Join(", ", unknown.Take(5)));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FactorySim.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("FactorySim.sln not found above the test directory.");
    }
}
