using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

/// <summary>Core helpers behind the editor and the tag panels: I/O list, history, copy, overlap, parameters, search.</summary>
public class EditorSupportTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static (Scene Scene, IReadOnlyList<IoPoint> Points) PusherLine()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Add(Catalog.Get("pusher"), 0, 1);
        scene.Add(Catalog.Get("diffuse_sensor"), 1, 1);
        IReadOnlyList<TagMapEntry> entries = SceneTags.Build(scene, Catalog);
        return (scene, IoList.Build(scene, Catalog, entries));
    }

    // ---- I/O list

    [Fact]
    public void Io_list_connects_every_tag_to_the_part_that_owns_it()
    {
        (Scene scene, IReadOnlyList<IoPoint> points) = PusherLine();

        IoPoint back = points.Single(point => point.Tag.Name == "Pusher back");
        Assert.Equal("Pusher", back.Part);
        Assert.Equal("back", back.Signal);
        Assert.Equal(scene.Items[1].Id, back.ItemId);
        Assert.Equal(Catalog.Get("pusher").Name, back.AssetName);

        IoPoint drive = points.Single(point => point.Tag.Name == "Conveyor");
        Assert.Equal("Conveyor", drive.Part);
        Assert.Equal(string.Empty, drive.Signal);
        Assert.Equal(TagDirection.Actuator, drive.Tag.Direction);
    }

    [Fact]
    public void Io_list_puts_the_fixed_tags_on_the_simulator_itself()
    {
        (_, IReadOnlyList<IoPoint> points) = PusherLine();

        foreach (string name in new[] { SceneTags.StartButton, SceneTags.StopButton, SceneTags.SimRunning, SceneTags.RunLamp })
        {
            IoPoint point = points.Single(p => p.Tag.Name == name);
            Assert.Null(point.ItemId);
            Assert.Equal(IoList.SimulatorPart, point.Part);
            Assert.Equal(name, point.Signal);
        }
    }

    [Fact]
    public void Io_list_gives_plc_inputs_and_outputs_with_their_addresses()
    {
        (_, IReadOnlyList<IoPoint> points) = PusherLine();

        IoPoint[] inputs = IoList.PlcInputs(points).ToArray();
        IoPoint[] outputs = IoList.PlcOutputs(points).ToArray();

        Assert.Equal(points.Count, inputs.Length + outputs.Length);
        Assert.All(inputs, point => Assert.StartsWith("%IX", point.Tag.IecAddress));
        Assert.All(outputs, point => Assert.StartsWith("%QX", point.Tag.IecAddress));
        Assert.Contains(inputs, point => point.Tag.Name == "Sensor");
        Assert.Contains(outputs, point => point.Tag.Name == "Pusher");
        Assert.Equal(inputs.OrderBy(p => p.Tag.Offset).Select(p => p.Tag.Name), inputs.Select(p => p.Tag.Name));
    }

    [Fact]
    public void Io_list_exports_as_csv_with_the_connection()
    {
        (_, IReadOnlyList<IoPoint> points) = PusherLine();

        string[] lines = IoList.ToCsv(points).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Kind;IecAddress;Modbus;Variable;Part;Signal;Asset;Type;Unit", lines[0]);
        Assert.Equal(points.Count + 1, lines.Length);
        Assert.Contains(lines, line => line.StartsWith("Input;%IX", StringComparison.Ordinal) && line.Contains(";Pusher;back;"));
        Assert.Contains(lines, line => line.StartsWith("Output;%QX", StringComparison.Ordinal) && line.Contains(";Conveyor;;"));
    }

    [Theory]
    [InlineData(TagFilter.All, "", 9)]
    [InlineData(TagFilter.Sensors, "", 6)]
    [InlineData(TagFilter.Actuators, "", 3)]
    [InlineData(TagFilter.All, "pusher", 3)]
    [InlineData(TagFilter.Sensors, "PUSHER", 2)]
    [InlineData(TagFilter.All, "%QX", 3)]
    public void Tags_can_be_filtered_by_direction_and_text(TagFilter filter, string search, int expected)
    {
        (_, IReadOnlyList<IoPoint> points) = PusherLine();

        Assert.Equal(expected, points.Count(point => IoList.Matches(point, filter, search, forced: false)));
    }

    [Fact]
    public void The_forced_filter_keeps_only_forced_tags()
    {
        (_, IReadOnlyList<IoPoint> points) = PusherLine();

        Assert.DoesNotContain(points, point => IoList.Matches(point, TagFilter.Forced, string.Empty, forced: false));
        Assert.All(points, point => Assert.True(IoList.Matches(point, TagFilter.Forced, string.Empty, forced: true)));
    }

    // ---- history

    [Fact]
    public void History_undoes_and_redoes_changes_to_a_scene()
    {
        var scene = new Scene();
        var history = new SceneHistory();
        Assert.False(history.CanUndo);

        history.Record(scene);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        history.Record(scene);
        scene.Add(Catalog.Get("pusher"), 0, 1);

        Scene? one = history.Undo(scene);
        Assert.NotNull(one);
        Assert.Single(one!.Items);
        Assert.True(history.CanRedo);

        Scene? none = history.Undo(one);
        Assert.Empty(none!.Items);
        Assert.Null(history.Undo(none));

        Scene? again = history.Redo(none);
        Assert.Single(again!.Items);
        Scene? both = history.Redo(again);
        Assert.Equal(2, both!.Items.Count);
        Assert.Null(history.Redo(both));
    }

    [Fact]
    public void A_new_change_after_undo_drops_the_redo_steps()
    {
        var scene = new Scene();
        var history = new SceneHistory();
        history.Record(scene);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        Scene undone = history.Undo(scene)!;

        history.Record(undone);
        undone.Add(Catalog.Get("pusher"), 0, 1);

        Assert.False(history.CanRedo);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void History_keeps_at_most_its_capacity()
    {
        var scene = new Scene();
        var history = new SceneHistory(capacity: 3);
        for (int i = 0; i < 10; i++)
        {
            history.Record(scene);
            scene.Add(Catalog.Get("guide_rail_1m"), i, 0);
        }

        int steps = 0;
        Scene? current = scene;
        while ((current = history.Undo(current!)) is not null)
        {
            steps++;
        }

        Assert.Equal(3, steps);
    }

    // ---- copy

    [Fact]
    public void Duplicate_copies_asset_height_yaw_and_params_under_a_new_name()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 1, 2, 90);
        scene.SetHeight(belt.Id, 1.9);
        scene.SetParam(belt.Id, "speed", 0.3);

        SceneItem copy = scene.Duplicate(belt.Id, 0.5, 0.25);

        Assert.NotEqual(belt.Id, copy.Id);
        Assert.Equal("Conveyor 2", copy.Name);
        Assert.Equal(belt.Asset, copy.Asset);
        Assert.Equal((1.5, 1.9, 2.25, 90.0), (copy.X, copy.Y, copy.Z, copy.Yaw));
        Assert.Equal(0.3, copy.Params!["speed"]);
        Assert.NotSame(scene.Find(belt.Id)!.Params, copy.Params);
        Assert.Equal(2, scene.Items.Count);
    }

    [Fact]
    public void Paste_places_a_copy_of_an_item_that_was_deleted_meanwhile()
    {
        var scene = new Scene();
        SceneItem pusher = scene.Add(Catalog.Get("pusher"), 0, 0);
        scene.Remove(pusher.Id);

        SceneItem pasted = scene.Paste(pusher, 3.1, 1.0);

        Assert.Equal("Pusher", pasted.Name);
        Assert.Equal((3.0, 1.0), (pasted.X, pasted.Z));
        Assert.Single(scene.Items);
    }

    // ---- overlap

    [Fact]
    public void Two_conveyors_through_each_other_are_reported()
    {
        var scene = new Scene();
        SceneItem a = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        SceneItem b = scene.Add(Catalog.Get("belt_conveyor_2m"), 0.5, 0);

        (SceneItem First, SceneItem Second) pair = Assert.Single(SceneOverlap.Find(scene, Catalog));

        Assert.Equal((a.Id, b.Id), (pair.First.Id, pair.Second.Id));
    }

    [Fact]
    public void Conveyors_end_to_end_crossing_at_another_height_or_carrying_small_parts_do_not_overlap()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 2, 0);                 // end to end
        SceneItem crossing = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0, 90);
        scene.SetHeight(crossing.Id, 1.9);                                // crosses a metre higher
        scene.Add(Catalog.Get("emitter_gantry"), -0.5, 0);                // stands over the belt
        scene.Add(Catalog.Get("diffuse_sensor"), 0, 0.5);

        Assert.Empty(SceneOverlap.Find(scene, Catalog));
    }

    [Fact]
    public void A_turned_conveyor_overlaps_by_its_turned_footprint()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 1.5, 90);           // 2 m long across: reaches z 0.5..2.5

        Assert.Empty(SceneOverlap.Find(scene, Catalog));

        scene.Add(Catalog.Get("belt_conveyor_2m"), 0.75, 0.75, 90);       // reaches z -0.25..1.75: through the first
        Assert.NotEmpty(SceneOverlap.Find(scene, Catalog));
    }

    // ---- parameters

    [Fact]
    public void A_conveyor_offers_speed_and_always_on()
    {
        IReadOnlyList<ParamField> fields = ItemParams.Editable(Catalog.Get("belt_conveyor_2m"));

        ParamField speed = fields.Single(field => field.Name == "speed");
        Assert.False(speed.IsToggle);
        Assert.Equal(Catalog.Get("belt_conveyor_2m").Param("speed", 0), speed.Default);
        Assert.True(fields.Single(field => field.Name == SceneTags.AlwaysOn).IsToggle);
        Assert.DoesNotContain(fields, field => field.Name is "length" or "width");
    }

    [Fact]
    public void Emitters_offer_interval_seed_and_the_box_mix_only_for_boxes()
    {
        string[] boxes = ItemParams.Editable(Catalog.Get("emitter_gantry")).Select(field => field.Name).ToArray();
        string[] pallets = ItemParams.Editable(Catalog.Get("emitter_pallet")).Select(field => field.Name).ToArray();

        Assert.Contains("interval", boxes);
        Assert.Contains("highRatio", boxes);
        Assert.Contains("seed", boxes);
        Assert.Contains("interval", pallets);
        Assert.DoesNotContain("highRatio", pallets);
    }

    [Fact]
    public void A_visual_asset_offers_no_parameters_and_a_sensor_offers_range_and_post()
    {
        Assert.Empty(ItemParams.Editable(Catalog.Get("electric_switchboard")));

        string[] sensor = ItemParams.Editable(Catalog.Get("diffuse_sensor")).Select(field => field.Name).ToArray();
        Assert.Contains("range", sensor);
        Assert.Contains("post", sensor);
    }

    [Fact]
    public void A_machining_center_offers_its_machine_time_and_swing_speed()
    {
        string[] fields = ItemParams.Editable(Catalog.Get("machining_center")).Select(field => field.Name).ToArray();

        Assert.Contains("machineTime", fields);
        Assert.Contains("swingSpeed", fields);
        Assert.DoesNotContain("reach", fields);
    }

    // ---- supports

    [Fact]
    public void A_raised_conveyor_gets_four_posts_and_a_ring_under_its_legs()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        Assert.Empty(SceneSupports.For(belt, Catalog.Get("belt_conveyor_2m")));       // stands on the floor

        belt = scene.SetHeight(belt.Id, 1.9);
        IReadOnlyList<SupportBeam> beams = SceneSupports.For(belt, Catalog.Get("belt_conveyor_2m"));

        SupportBeam[] posts = beams.Where(beam => beam.Axis == 'Y').ToArray();
        Assert.Equal(4, posts.Length);
        Assert.All(posts, post => Assert.Equal(-1.9, post.Y, 6));                   // from the floor
        Assert.All(posts, post => Assert.Equal(1.1 - SceneSupports.BeamSize, post.Length, 6));
        Assert.Equal(2, beams.Count(beam => beam.Axis == 'X'));
        Assert.Equal(2, beams.Count(beam => beam.Axis == 'Z'));
        // The ring ends exactly where the legs of the model end (0.8 m under the belt top).
        Assert.All(beams.Where(beam => beam.Axis != 'Y'), beam => Assert.Equal(-0.8 - SceneSupports.BeamSize / 2, beam.Y, 6));
        Assert.All(posts, post => Assert.InRange(post.X, -1.0, 1.0));
    }

    [Fact]
    public void Sensors_and_stations_get_no_support_frame()
    {
        var scene = new Scene();
        SceneItem sensor = scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);
        SceneItem lift = scene.Add(Catalog.Get("elevator"), 2, 0);
        scene.SetHeight(lift.Id, 1.0);

        Assert.Empty(SceneSupports.For(sensor, Catalog.Get("diffuse_sensor")));
        Assert.Empty(SceneSupports.For(scene.Find(lift.Id)!, Catalog.Get("elevator")));
        Assert.True(Catalog.Get("support_beam").Internal);
    }

    // ---- search

    [Fact]
    public void Search_finds_placeable_assets_by_name_id_or_category()
    {
        Assert.Contains(Catalog.Search("pallet"), asset => asset.Id == "palletizer");
        Assert.Contains(Catalog.Search("pallet"), asset => asset.Id == "emitter_pallet");
        Assert.DoesNotContain(Catalog.Search("pallet"), asset => asset.Id == "pallet");           // items are not placeable
        Assert.Contains(Catalog.Search("TURNTABLE"), asset => asset.Behaviour == AssetBehaviours.Turntable);
        Assert.All(Catalog.Search("band", "light_load"), asset => Assert.Equal("light_load", asset.Category));
        Assert.Equal(Catalog.Assets.Count(asset => asset.IsPlaceable), Catalog.Search("  ").Count);
        Assert.Empty(Catalog.Search("bestaat-niet"));
    }

    [Fact]
    public void Search_matches_the_category_name()
    {
        string category = Catalog.Categories.Single(c => c.Id == "warning_devices").Name;   // whatever the label is
        Assert.Contains(Catalog.Search(category), asset => asset.Id == "stack_light");
        Assert.All(Catalog.Search(category), asset => Assert.Equal("warning_devices", asset.Category));
    }
}
