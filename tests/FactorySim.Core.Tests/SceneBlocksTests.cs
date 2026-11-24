using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 18: building blocks — a group of items saved as a <c>.fsblock.json</c> and placed again.</summary>
public class SceneBlocksTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static Scene Mvp() =>
        Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));

    private static readonly string[] StationIds =
    {
        "main-belt", "side-lane", "rail-back-a", "rail-back-b", "rail-front-a", "rail-front-b",
        "lane-rail-a", "lane-rail-b", "pusher", "eye-low", "eye-high",
    };

    [Fact]
    public void A_block_stores_the_items_relative_to_the_centre_of_the_group()
    {
        Scene scene = Mvp();

        SceneBlock block = SceneBlocks.FromSelection(scene, StationIds, "Pusherstation", "Band, rails, pusher en twee photo-eyes");

        Assert.Equal("Pusherstation", block.Name);
        Assert.Equal(11, block.Items.Count);
        (double cx, double cz) = scene.Centre(StationIds);
        SceneItem pusher = block.Items.Single(item => item.Asset == "pusher");
        Assert.Equal(1.0 - cx, pusher.X, 6);
        Assert.Equal(-0.48 - cz, pusher.Z, 6);
        Assert.Equal(0.945, pusher.Y, 6);                           // height stays absolute
    }

    [Fact]
    public void A_block_survives_the_round_trip_with_params_emits_and_poses()
    {
        var scene = new Scene();
        SceneItem emitter = scene.Add(Catalog.Get("emitter_gantry"), -1, 0);
        scene.SetParam(emitter.Id, "interval", 3.5);
        scene.SetEmits(emitter.Id, "pallet");
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_4m"), 0, 0);
        SceneItem robot = scene.Add(Catalog.Get("robot_6axis"), 0, 2);
        scene.SetPoses(robot.Id, new[] { new RobotPose(1, "Boven", new FactorySim.Core.Robot.RobotAngles(0, -30, 30, 0, 45, 0)) });

        SceneBlock block = SceneBlocks.FromSelection(scene, new[] { emitter.Id, belt.Id, robot.Id }, "Invoer", "Test");
        string json = SceneBlocks.ToJson(block);
        SceneBlock back = SceneBlocks.FromJson(json);

        Assert.Contains("\"factorysim-block\"", json);
        Assert.DoesNotContain("\"tags\"", json);
        Assert.Equal("Invoer", back.Name);
        Assert.Equal("Test", back.Description);
        Assert.Equal(3, back.Items.Count);
        SceneItem e = back.Items.Single(item => item.Asset == "emitter_gantry");
        Assert.Equal(3.5, e.Params!["interval"]);
        Assert.Equal("pallet", e.Emits);
        SceneItem r = back.Items.Single(item => item.Asset == "robot_6axis");
        Assert.Equal("Boven", r.Poses!.Single().Name);
        Assert.Equal(45.0, r.Poses![0].Angles.A5, 6);
    }

    [Fact]
    public void Placing_a_block_twice_gives_unique_names_and_tags_and_no_overlap_between_the_copies()
    {
        SceneBlock block = SceneBlocks.FromSelection(Mvp(), StationIds, "Pusherstation", "");
        var scene = new Scene();
        scene.SetFloor(20, 14);

        BlockPlacement first = SceneBlocks.Place(scene, block, Catalog, 0, -3.5, 0);
        BlockPlacement second = SceneBlocks.Place(scene, block, Catalog, 0, 3, 0);

        Assert.Equal(11, first.Items.Count);
        Assert.Equal(11, second.Items.Count);
        Assert.Empty(first.Skipped);
        Assert.Equal(22, scene.Items.Select(item => item.Name).Distinct().Count());
        Assert.Equal(22, scene.Items.Select(item => item.Id).Distinct().Count());
        Assert.Empty(SceneTags.Problems(scene, Catalog));
        Assert.Contains(scene.Items, item => item.Name == "Pusher 2");
        var firstIds = first.Items.Select(item => item.Id).ToHashSet();
        Assert.DoesNotContain(SceneOverlap.Find(scene, Catalog),
            pair => firstIds.Contains(pair.First.Id) != firstIds.Contains(pair.Second.Id));
    }

    [Fact]
    public void Placing_puts_the_centre_of_the_group_at_the_point_and_keeps_the_layout()
    {
        Scene mvp = Mvp();
        SceneBlock block = SceneBlocks.FromSelection(mvp, StationIds, "Pusherstation", "");
        (double cx, double cz) = mvp.Centre(StationIds);
        var scene = new Scene();

        SceneBlocks.Place(scene, block, Catalog, cx, cz, 0);

        foreach (string id in StationIds)
        {
            SceneItem original = mvp.Find(id)!;
            SceneItem placed = scene.Items.Single(item => item.Name == original.Name);
            Assert.Equal((original.X, original.Y, original.Z, original.Yaw), (placed.X, placed.Y, placed.Z, placed.Yaw));
            Assert.Equal(original.Params?.Count ?? 0, placed.Params?.Count ?? 0);
        }
    }

    [Fact]
    public void Turning_at_placing_turns_the_whole_group_about_its_centre()
    {
        var source = new Scene();
        SceneItem a = source.Add(Catalog.Get("belt_conveyor_2m"), -1, 0);
        SceneItem b = source.Add(Catalog.Get("belt_conveyor_2m"), 1, 0);
        SceneBlock block = SceneBlocks.FromSelection(source, new[] { a.Id, b.Id }, "Twee banden", "");
        var scene = new Scene();

        BlockPlacement placed = SceneBlocks.Place(scene, block, Catalog, 2, 1, 90);

        // A positive turn takes +X towards -Z: the belt that lay 1 m left of the centre now lies 1 m in +Z.
        Assert.Equal((2.0, 2.0, 90.0), (placed.Items[0].X, placed.Items[0].Z, placed.Items[0].Yaw));
        Assert.Equal((2.0, 0.0, 90.0), (placed.Items[1].X, placed.Items[1].Z, placed.Items[1].Yaw));
    }

    [Fact]
    public void A_retroreflective_sensor_in_a_block_finds_its_own_reflector_after_placing_twice()
    {
        var source = new Scene();
        SceneItem sensor = source.Add(Catalog.Get("retroreflective_sensor"), 0, 0, 180);
        SceneItem reflector = source.Add(Catalog.Get("reflector"), 0, -1.5);
        SceneBlock block = SceneBlocks.FromSelection(source, new[] { sensor.Id, reflector.Id }, "Lichtsluis", "");
        var scene = new Scene();

        BlockPlacement first = SceneBlocks.Place(scene, block, Catalog, -3, 0, 0);
        BlockPlacement second = SceneBlocks.Place(scene, block, Catalog, 3, 0, 0);

        IReadOnlyList<SensorLink> links = SensorPairing.Links(scene, Catalog);
        Assert.Equal(2, links.Count);
        foreach (BlockPlacement placement in new[] { first, second })
        {
            var ids = placement.Items.Select(item => item.Id).ToHashSet();
            SensorLink link = links.Single(l => ids.Contains(l.SensorId));
            Assert.Contains(link.TargetId, ids);
        }
    }

    [Fact]
    public void An_asset_that_is_not_in_the_library_is_skipped_and_reported()
    {
        string json = """
            {
              "format": "factorysim-block", "version": 1, "name": "Oud", "description": "",
              "items": [
                { "id": "a", "asset": "belt_conveyor_2m", "name": "Conveyor", "position": [0, 0.8, 0], "yaw": 0 },
                { "id": "b", "asset": "teleporter", "name": "Teleporter", "position": [1.5, 0.8, 0], "yaw": 0 }
              ]
            }
            """;
        SceneBlock block = SceneBlocks.FromJson(json);
        var scene = new Scene();

        BlockPlacement placed = SceneBlocks.Place(scene, block, Catalog, 0, 0, 0);

        Assert.Single(placed.Items);
        Assert.Equal(new[] { "Teleporter" }, placed.Skipped);
        Assert.Single(scene.Items);
    }

    [Fact]
    public void A_block_from_a_newer_version_or_another_format_is_refused()
    {
        Assert.Throws<SceneException>(() => SceneBlocks.FromJson("""{ "format": "factorysim-block", "version": 99, "name": "x", "items": [] }"""));
        Assert.Throws<SceneException>(() => SceneBlocks.FromJson("""{ "format": "factorysim-scene", "version": 1, "name": "x", "items": [] }"""));
        Assert.Throws<SceneException>(() => SceneBlocks.FromJson("not json"));
    }

    [Fact]
    public void A_block_needs_at_least_one_item_and_a_name()
    {
        Scene scene = Mvp();

        Assert.Throws<SceneException>(() => SceneBlocks.FromSelection(scene, Array.Empty<string>(), "Leeg", ""));
        Assert.Throws<SceneException>(() => SceneBlocks.FromSelection(scene, StationIds, "  ", ""));
    }

    [Theory]
    [InlineData("Pusherstation", "pusherstation")]
    [InlineData("Sorteer punt / 2", "sorteer_punt_2")]
    [InlineData("***", "blok")]
    public void The_file_name_of_a_block_follows_from_its_name(string name, string file)
    {
        Assert.Equal(file + SceneBlocks.Extension, SceneBlocks.FileName(name));
    }

    [Theory]
    [InlineData("pusherstation")]
    [InlineData("sorteerpunt")]
    [InlineData("invoer")]
    public void The_example_blocks_load_and_place_without_problems_or_overlap(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "blocks", file + SceneBlocks.Extension);
        SceneBlock block = SceneBlocks.FromJson(File.ReadAllText(path));
        var scene = new Scene();

        BlockPlacement placed = SceneBlocks.Place(scene, block, Catalog, 0, 0, 0);

        Assert.False(string.IsNullOrWhiteSpace(block.Description));
        Assert.True(placed.Items.Count >= 3);
        Assert.Empty(placed.Skipped);
        Assert.Empty(SceneTags.Problems(scene, Catalog));
        Assert.Empty(SceneOverlap.Find(scene, Catalog));
        Assert.All(scene.Items, item => Assert.True(scene.OnFloor(item.X, item.Z), item.Name));
    }
}
