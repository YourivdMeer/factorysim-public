using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 20: the scene gallery, a card per scene with a floor plan, a name and a description.</summary>
public class SceneSummaryTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static readonly DateTime FileTime = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Local);

    private static string Example(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name + ".fsscene.json"));

    // ---- the summary

    [Fact]
    public void The_mvp_scene_has_fourteen_items_eleven_tags_and_a_description()
    {
        SceneSummary summary = SceneSummary.FromJson(Example("mvp_sorting"), Catalog, FileTime, "mvp_sorting.fsscene.json");

        Assert.Null(summary.Error);
        Assert.Equal("MVP — sorteren op hoogte", summary.Name);
        Assert.Equal(14, summary.ItemCount);
        Assert.Equal(11, summary.TagCount);
        Assert.Equal(14, summary.Footprints.Count);
        Assert.False(string.IsNullOrWhiteSpace(summary.Description));
        Assert.Equal(Scene.DefaultFloor, summary.Floor);
        Assert.Equal(FileTime, summary.FileTime);
        Assert.Empty(summary.UnknownAssets);
    }

    [Fact]
    public void The_assembly_example_also_has_a_description()
    {
        SceneSummary summary = SceneSummary.FromJson(Example("assembly_line"), Catalog, FileTime, "assembly_line.fsscene.json");

        Assert.Null(summary.Error);
        Assert.False(string.IsNullOrWhiteSpace(summary.Description));
    }

    [Fact]
    public void The_footprint_of_a_turned_belt_lies_across_the_floor()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 1.0, 2.0);
        scene.Rotate(belt.Id, 90.0);

        SceneSummary summary = SceneSummary.From(scene, Catalog, FileTime);
        ItemFootprint footprint = Assert.Single(summary.Footprints);

        double minX = footprint.Corners.Min(corner => corner.X);
        double maxX = footprint.Corners.Max(corner => corner.X);
        double minZ = footprint.Corners.Min(corner => corner.Z);
        double maxZ = footprint.Corners.Max(corner => corner.Z);

        // A 2 m belt turned a quarter: about 2 m along Z, much less along X, around (1, 2).
        Assert.InRange(maxZ - minZ, 1.9, 2.2);
        Assert.InRange(maxX - minX, 0.9, 1.2);                          // the width of the belt with its motor
        Assert.InRange((minX + maxX) / 2.0, 0.6, 1.4);
        Assert.InRange((minZ + maxZ) / 2.0, 1.6, 2.4);
        Assert.Equal(FootprintKind.Transport, footprint.Kind);
    }

    [Fact]
    public void The_colour_group_follows_the_category()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);
        scene.Add(Catalog.Get("palletizer"), 3, 0);
        scene.Add(Catalog.Get("emitter_gantry"), -3, 0);

        SceneSummary summary = SceneSummary.From(scene, Catalog, null);

        Assert.Equal(new[] { FootprintKind.Sensor, FootprintKind.Station, FootprintKind.Other }, summary.Footprints.Select(f => f.Kind));
    }

    [Fact]
    public void A_scene_without_a_description_has_an_empty_one_and_does_not_write_it()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);

        Assert.DoesNotContain("description", scene.ToJson());
        Assert.Equal(string.Empty, Scene.FromJson(scene.ToJson()).Description);
        Assert.Equal(string.Empty, SceneSummary.From(scene, Catalog, null).Description);
    }

    [Fact]
    public void A_broken_file_gives_a_summary_with_an_error_instead_of_an_exception()
    {
        SceneSummary summary = SceneSummary.FromJson("{ dit is geen scène", Catalog, FileTime, "kapot.fsscene.json");

        Assert.NotNull(summary.Error);
        Assert.Equal("kapot.fsscene.json", summary.Name);
        Assert.Empty(summary.Footprints);
        Assert.Equal(0, summary.ItemCount);
    }

    [Fact]
    public void A_file_of_another_format_is_also_an_error()
    {
        Assert.NotNull(SceneSummary.FromJson("{ \"format\": \"iets-anders\", \"version\": 1 }", Catalog, null, "x.json").Error);
    }

    [Fact]
    public void An_unknown_asset_does_not_count_and_is_reported()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        string json = scene.ToJson().Replace("\"asset\": \"belt_conveyor_2m\"", "\"asset\": \"bestaat_niet\"");

        var mixed = Scene.FromJson(json);
        mixed.Add(Catalog.Get("belt_conveyor_4m"), 3, 0);
        SceneSummary summary = SceneSummary.From(mixed, Catalog, null);

        Assert.Null(summary.Error);
        Assert.Equal(1, summary.ItemCount);
        Assert.Single(summary.Footprints);
        Assert.Equal(new[] { "bestaat_niet" }, summary.UnknownAssets);
        Assert.Contains("bestaat_niet", summary.Warning);
    }

    [Fact]
    public void The_preview_is_the_first_two_lines_of_the_description()
    {
        var scene = new Scene();
        scene.SetDescription("Regel een\nRegel twee\nRegel drie");

        Assert.Equal("Regel een\nRegel twee", SceneSummary.From(scene, Catalog, null).DescriptionPreview);
    }

    // ---- the description field

    [Fact]
    public void A_description_is_saved_and_opened_again_and_trimmed_and_cut_off()
    {
        var scene = new Scene();
        scene.SetDescription("  Sorteer op hoogte.\r\nTweede regel é  ");

        Assert.Equal("Sorteer op hoogte.\nTweede regel é", Scene.FromJson(scene.ToJson()).Description);

        scene.SetDescription(new string('a', Scene.MaxDescription + 50));
        Assert.Equal(Scene.MaxDescription, scene.Description.Length);

        scene.SetDescription(null);
        Assert.Equal(string.Empty, scene.Description);
    }

    [Fact]
    public void An_undo_snapshot_brings_back_the_description_from_before()
    {
        var scene = new Scene();
        scene.SetDescription("Oud");
        var history = new SceneHistory();

        history.Record(scene);
        scene.SetDescription("Nieuw");
        Scene? undone = history.Undo(scene);

        Assert.Equal("Oud", undone!.Description);
        Assert.Equal("Nieuw", history.Redo(undone)!.Description);
    }

    // ---- searching

    [Fact]
    public void Search_finds_name_and_description_without_case()
    {
        var first = new Scene { Name = "Sorteerlijn" };
        var second = new Scene { Name = "Assemblage" };
        second.SetDescription("Basis en deksel worden een eindproduct.");
        SceneSummary[] all = { SceneSummary.From(first, Catalog, null), SceneSummary.From(second, Catalog, null) };

        Assert.Equal(new[] { "Sorteerlijn" }, SceneSummary.Search(all, "SORTEER").Select(s => s.Name));
        Assert.Equal(new[] { "Assemblage" }, SceneSummary.Search(all, "deksel").Select(s => s.Name));
        Assert.Equal(2, SceneSummary.Search(all, "  ").Count);
    }
}
