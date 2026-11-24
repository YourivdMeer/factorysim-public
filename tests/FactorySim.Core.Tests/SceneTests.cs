using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class SceneTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static Scene LoadMvp() =>
        Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));

    // ---- catalogue

    [Fact]
    public void Catalogue_lists_every_category_and_no_asset_twice()
    {
        Assert.Equal(9, Catalog.Categories.Count);
        Assert.True(Catalog.Assets.Count >= 90);
        Assert.Equal(Catalog.Assets.Count, Catalog.Assets.Select(asset => asset.Id).Distinct().Count());
        Assert.All(Catalog.Assets, asset => Assert.Contains(Catalog.Categories, category => category.Id == asset.Category));
    }

    [Fact]
    public void Catalogue_describes_a_conveyor_with_its_module_size()
    {
        CatalogAsset belt = Catalog.Get("belt_conveyor_4m");

        Assert.Equal("light_load", belt.Category);
        Assert.Equal(AssetBehaviours.Conveyor, belt.Behaviour);
        Assert.Equal(4.0, belt.Param("length", 0.0));
        Assert.Equal(0.8, belt.OriginHeight);
        Assert.Contains("Belt", belt.Parts);
        Assert.True(belt.IsFunctional && belt.IsPlaceable);
    }

    [Fact]
    public void Every_asset_stays_within_the_triangle_budget()
    {
        Assert.All(Catalog.Assets, asset => Assert.InRange(asset.Triangles, 1, 5000));
    }

    [Fact]
    public void Items_and_internal_assets_cannot_be_placed()
    {
        Assert.False(Catalog.Get("box_small").IsPlaceable);
        Assert.False(Catalog.Get("rail_bracket").IsPlaceable);
        Assert.DoesNotContain(Catalog.Placeable("items"), _ => true);
        Assert.Contains(Catalog.Placeable("stations"), asset => asset.Id == "tank");
    }

    [Fact]
    public void Unknown_asset_and_broken_catalogue_are_reported()
    {
        Assert.Null(Catalog.Find("nope"));
        Assert.Throws<CatalogException>(() => Catalog.Get("nope"));
        Assert.Throws<CatalogException>(() => AssetCatalog.FromJson("{}"));
        Assert.Throws<CatalogException>(() => AssetCatalog.FromJson("not json"));
    }

    // ---- editing

    [Fact]
    public void Adding_snaps_to_the_grid_and_puts_the_origin_at_the_right_height()
    {
        var scene = new Scene();

        SceneItem item = scene.Add(Catalog.Get("belt_conveyor_2m"), 1.13, -0.38, 90);

        Assert.Equal(1.25, item.X);
        Assert.Equal(-0.5, item.Z);
        Assert.Equal(0.8, item.Y);
        Assert.Equal(90, item.Yaw);
        Assert.Equal("Conveyor", item.Name);
    }

    [Fact]
    public void Names_and_ids_stay_unique()
    {
        var scene = new Scene();
        CatalogAsset belt = Catalog.Get("belt_conveyor_2m");

        SceneItem a = scene.Add(belt, 0, 0);
        SceneItem b = scene.Add(belt, 2, 0);
        SceneItem c = scene.Add(belt, 4, 0);

        Assert.Equal(new[] { "Conveyor", "Conveyor 2", "Conveyor 3" }, new[] { a.Name, b.Name, c.Name });
        Assert.Equal(3, new[] { a.Id, b.Id, c.Id }.Distinct().Count());
    }

    [Fact]
    public void Move_rotate_rename_and_remove()
    {
        var scene = new Scene();
        SceneItem item = scene.Add(Catalog.Get("pusher"), 0, 0);

        Assert.Equal(2.5, scene.Move(item.Id, 2.6, 0).X);
        Assert.Equal(90, scene.Rotate(item.Id).Yaw);
        Assert.Equal(0, scene.Rotate(item.Id, 270).Yaw);
        Assert.Equal(270, scene.Rotate(item.Id, -90).Yaw);
        Assert.Equal("Duwer", scene.Rename(item.Id, " Duwer ").Name);
        Assert.True(scene.Remove(item.Id));
        Assert.False(scene.Remove(item.Id));
        Assert.Empty(scene.Items);
    }

    [Fact]
    public void Renaming_to_a_used_or_empty_name_is_rejected()
    {
        var scene = new Scene();
        SceneItem a = scene.Add(Catalog.Get("pusher"), 0, 0);
        SceneItem b = scene.Add(Catalog.Get("pusher"), 2, 0);

        Assert.Throws<SceneException>(() => scene.Rename(b.Id, a.Name));
        Assert.Throws<SceneException>(() => scene.Rename(b.Id, "  "));
        Assert.Throws<SceneException>(() => scene.Move("missing", 0, 0));
        Assert.Throws<SceneException>(() => scene.Add(Catalog.Get("box_small"), 0, 0));
    }

    // ---- picking

    [Fact]
    public void Picking_finds_the_item_under_a_point_also_when_it_is_rotated()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_4m"), 0, 0, 90);      // now runs along Z

        Assert.Equal(belt.Id, ScenePicking.ItemAt(scene, Catalog, 0.2, 1.8)?.Id);
        Assert.Equal(belt.Id, ScenePicking.ItemAt(scene, Catalog, -0.3, -1.9)?.Id);
        Assert.Null(ScenePicking.ItemAt(scene, Catalog, 1.8, 0.0));                 // would hit it unrotated
        Assert.Null(ScenePicking.ItemAt(scene, Catalog, 5.0, 5.0));
    }

    [Fact]
    public void Picking_prefers_the_smaller_item_where_two_overlap()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_4m"), 0, 0);
        SceneItem sensor = scene.Add(Catalog.Get("diffuse_sensor"), 0.5, -0.25);

        Assert.Equal(sensor.Id, ScenePicking.ItemAt(scene, Catalog, 0.5, -0.25)?.Id);
    }

    // ---- save / load

    [Fact]
    public void Json_round_trip_keeps_items_parameters_and_tags()
    {
        var scene = new Scene { Name = "Test" };
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_4m"), 1, 2, 180);
        scene.SetParam(belt.Id, SceneTags.AlwaysOn, 1);
        scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);
        scene.Tags.AddRange(SceneTags.Build(scene, Catalog));

        Scene again = Scene.FromJson(scene.ToJson());

        Assert.Equal("Test", again.Name);
        Assert.Equal(scene.Items.Count, again.Items.Count);
        SceneItem loaded = again.Find(belt.Id)!;
        Assert.Equal((1.0, 0.8, 2.0, 180.0), (loaded.X, loaded.Y, loaded.Z, loaded.Yaw));
        Assert.Equal(1.0, loaded.Params![SceneTags.AlwaysOn]);
        Assert.Equal(scene.Tags, again.Tags);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{ \"format\": \"something-else\", \"version\": 1 }")]
    [InlineData("{ \"format\": \"factorysim-level\", \"version\": 99, \"items\": [] }")]
    [InlineData("{ \"format\": \"factorysim-level\", \"version\": 1, \"items\": [ { \"id\": \"a\", \"asset\": \"x\" } ] }")]
    [InlineData("{ \"format\": \"factorysim-level\", \"version\": 1, \"items\": [ { \"id\": \"a\", \"asset\": \"x\", \"position\": [0,0,0] }, { \"id\": \"a\", \"asset\": \"x\", \"position\": [0,0,0] } ] }")]
    public void Files_that_are_not_valid_scenes_are_rejected(string json)
    {
        Assert.Throws<SceneException>(() => Scene.FromJson(json));
    }

    // ---- tags

    [Fact]
    public void Each_kind_of_item_contributes_its_tags()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Add(Catalog.Get("pusher"), 2, 0);
        scene.Add(Catalog.Get("diffuse_sensor"), 4, 0);
        scene.Add(Catalog.Get("emitter_gantry"), 6, 0);
        scene.Add(Catalog.Get("remover_hood_1m0"), 8, 0);
        scene.Add(Catalog.Get("electric_switchboard"), 10, 0);      // visual only: no tags

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(
            new[] { "Start button", "Stop button", "Pusher back", "Pusher front", "Sensor", "Sim running" },
            tags.Where(tag => tag.Direction == TagDirection.Sensor).Select(tag => tag.Name));
        Assert.Equal(
            new[] { "Conveyor", "Pusher", "Emitter", "Run lamp" },
            tags.Where(tag => tag.Direction == TagDirection.Actuator).Select(tag => tag.Name));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, tags.Where(tag => tag.Area == ModbusArea.Coil).Select(tag => tag.Offset));
        TagMapEntry sensor = tags.Single(tag => tag.Name == "Pusher back");
        Assert.Equal("%IX0.2", sensor.IecAddress);
        Assert.Equal("IO.iPusherBack", sensor.Variable);
        _ = new TagMap(tags);      // the result is a valid tag map
    }

    [Theory]
    [InlineData("stop_blade", AssetBehaviours.Stop, "Stop")]
    [InlineData("roller_stop", AssetBehaviours.Stop, "Stop")]
    [InlineData("belt_conveyor_curved", AssetBehaviours.Curve, "Curve")]
    [InlineData("roller_conveyor_curved", AssetBehaviours.Curve, "Curve")]
    public void Stops_and_curves_are_functional_with_one_actuator_tag(string assetId, string behaviour, string name)
    {
        CatalogAsset asset = Catalog.Get(assetId);
        var scene = new Scene();

        SceneItem item = scene.Add(asset, 0, 0);
        TagMapEntry tag = Assert.Single(SceneTags.Build(scene, Catalog), t => t.Name == name);

        Assert.Equal(behaviour, asset.Behaviour);
        Assert.Equal(name, item.Name);
        Assert.Equal(TagDirection.Actuator, tag.Direction);
        Assert.Equal("IO.o" + name, tag.Variable);
    }

    [Theory]
    [InlineData("push_button_green", "Button")]
    [InlineData("emergency_stop", "Emergency stop")]
    [InlineData("selector", "Selector")]
    public void Buttons_give_one_sensor_tag(string assetId, string name)
    {
        var scene = new Scene();

        SceneItem item = scene.Add(Catalog.Get(assetId), 0, 0);
        TagMapEntry tag = Assert.Single(SceneTags.Build(scene, Catalog), t => t.Name == name);

        Assert.Equal(AssetBehaviours.Button, Catalog.Get(assetId).Behaviour);
        Assert.Equal(name, item.Name);
        Assert.Equal(TagDirection.Sensor, tag.Direction);
        Assert.Equal(ModbusArea.Coil, tag.Area);
    }

    [Fact]
    public void The_catalogue_says_how_each_control_behaves()
    {
        Assert.Equal(0.0, Catalog.Get("push_button_green").Param("latching", -1.0));
        Assert.Equal(0.0, Catalog.Get("push_button_green").Param("normallyClosed", 0.0));
        Assert.Equal(1.0, Catalog.Get("emergency_stop").Param("latching", -1.0));
        Assert.Equal(1.0, Catalog.Get("emergency_stop").Param("normallyClosed", 0.0));
        Assert.Equal(1.0, Catalog.Get("selector").Param("knob", 0.0));
    }

    [Fact]
    public void A_single_lamp_gives_one_actuator_tag_named_after_the_item()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("light_indicator_green"), 0, 0);
        scene.Add(Catalog.Get("warning_light"), 1, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Lamp").Direction);
        Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Warning light").Direction);
    }

    [Fact]
    public void A_stack_light_gives_one_actuator_tag_per_colour()
    {
        var scene = new Scene();
        SceneItem stack = scene.Add(Catalog.Get("stack_light"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Stack light", stack.Name);
        Assert.Equal(
            new[] { "Stack light green", "Stack light red", "Stack light yellow" },
            tags.Where(tag => tag.Name.StartsWith("Stack light")).Select(tag => tag.Name).OrderBy(n => n));
        Assert.All(tags.Where(tag => tag.Name.StartsWith("Stack light")), tag => Assert.Equal(TagDirection.Actuator, tag.Direction));
        Assert.Equal("IO.oStackLightGreen", tags.Single(tag => tag.Name == "Stack light green").Variable);
    }

    [Fact]
    public void Lamp_parts_of_an_asset_are_listed_with_their_tag_suffix()
    {
        Assert.Equal(new[] { ("Lamp", "") }, SceneTags.LampParts(Catalog.Get("light_indicator_red")));
        Assert.Equal(
            new[] { ("Lamp_Green", " green"), ("Lamp_Red", " red"), ("Lamp_Yellow", " yellow") },
            SceneTags.LampParts(Catalog.Get("stack_light")));
    }

    [Fact]
    public void A_wheel_sorter_gives_a_tag_for_straight_on_and_one_for_each_side()
    {
        var scene = new Scene();
        SceneItem sorter = scene.Add(Catalog.Get("popup_wheel_sorter"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Wheel sorter", sorter.Name);
        Assert.Equal(AssetBehaviours.WheelSorter, Catalog.Get("popup_wheel_sorter").Behaviour);
        foreach (string name in new[] { "Wheel sorter", "Wheel sorter left", "Wheel sorter right" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == name).Direction);
        }

        Assert.Equal("IO.oWheelSorterLeft", tags.Single(tag => tag.Name == "Wheel sorter left").Variable);
    }

    [Fact]
    public void A_chain_transfer_has_the_tags_of_a_wheel_sorter_and_carries_off_square()
    {
        var scene = new Scene();
        SceneItem transfer = scene.Add(Catalog.Get("chain_transfer"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Chain transfer", transfer.Name);
        Assert.Equal(90.0, Catalog.Get("chain_transfer").Param("angle", 0), 3);
        Assert.Equal(3, tags.Count(tag => tag.Name.StartsWith("Chain transfer") && tag.Direction == TagDirection.Actuator));
        Assert.Contains(tags, tag => tag.Name == "Chain transfer left");
    }

    [Fact]
    public void A_turntable_gives_roll_reverse_and_turn_tags_and_two_limit_switches()
    {
        var scene = new Scene();
        SceneItem table = scene.Add(Catalog.Get("turntable"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Turntable", table.Name);
        Assert.Equal(AssetBehaviours.Turntable, Catalog.Get("turntable").Behaviour);
        foreach (string name in new[] { "Turntable", "Turntable reverse", "Turntable turn" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == name).Direction);
        }

        foreach (string name in new[] { "Turntable at 0", "Turntable at 90" })
        {
            Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == name).Direction);
        }

        Assert.Equal("IO.iTurntableAt90", tags.Single(tag => tag.Name == "Turntable at 90").Variable);
    }

    [Fact]
    public void An_item_can_be_set_to_another_height()
    {
        var scene = new Scene();
        SceneItem sensor = scene.Add(Catalog.Get("diffuse_sensor"), 1, 2);

        SceneItem lowered = scene.SetHeight(sensor.Id, 0.6);

        Assert.Equal(0.6, lowered.Y);
        Assert.Equal(0.6, scene.Find(sensor.Id)!.Y);
        Assert.Equal(0.6, Scene.FromJson(scene.ToJson()).Items[0].Y);
        Assert.Throws<SceneException>(() => scene.SetHeight(sensor.Id, -0.1));
    }

    [Fact]
    public void A_two_axis_pick_and_place_gives_tags_for_x_z_and_the_gripper()
    {
        var scene = new Scene();
        SceneItem station = scene.Add(Catalog.Get("two_axis_pick_and_place"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);
        string[] own = tags.Where(tag => tag.Name.StartsWith("Pick and place")).Select(tag => tag.Name).ToArray();

        Assert.Equal("Pick and place", station.Name);
        Assert.Equal(AssetBehaviours.PickPlace, Catalog.Get("two_axis_pick_and_place").Behaviour);
        Assert.Equal(
            new[]
            {
                "Pick and place X", "Pick and place Z", "Pick and place grab",
                "Pick and place moving X", "Pick and place moving Z", "Pick and place item",
            },
            SceneTags.TagsOf(station, Catalog.Get("two_axis_pick_and_place")).Select(tag => tag.Name));
        Assert.Equal(6, own.Length);
        Assert.All(tags.Where(tag => own.Contains(tag.Name)), tag => Assert.Equal(
            tag.Name.Contains("moving") || tag.Name.EndsWith("item") ? TagDirection.Sensor : TagDirection.Actuator,
            tag.Direction));
        Assert.Equal("IO.iPickAndPlaceMovingX", tags.Single(tag => tag.Name == "Pick and place moving X").Variable);
    }

    [Fact]
    public void A_three_axis_pick_and_place_also_has_the_y_axis()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("pick_and_place"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Pick and place Y").Direction);
        Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == "Pick and place moving Y").Direction);
        Assert.Equal(8, tags.Count(tag => tag.Name.StartsWith("Pick and place")));
    }

    [Fact]
    public void Pick_and_place_stations_carry_the_dimensions_the_simulator_needs()
    {
        CatalogAsset two = Catalog.Get("two_axis_pick_and_place");
        CatalogAsset three = Catalog.Get("pick_and_place");

        Assert.Equal(-0.5, two.Param("restX", 0), 3);
        Assert.Equal(1.0, two.Param("strokeX", 0), 3);
        Assert.Equal(0.0, two.Param("strokeY", -1), 3);
        Assert.Equal(1.22, two.Param("tipHeight", 0), 3);
        Assert.Equal(1.5, three.Param("strokeX", 0), 3);
        Assert.Equal(0.5, three.Param("strokeY", 0), 3);
    }

    [Fact]
    public void An_elevator_gives_up_roll_and_reverse_tags_and_two_limit_switches()
    {
        var scene = new Scene();
        SceneItem lift = scene.Add(Catalog.Get("elevator"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Elevator", lift.Name);
        Assert.Equal(AssetBehaviours.Elevator, Catalog.Get("elevator").Behaviour);
        foreach (string name in new[] { "Elevator", "Elevator reverse", "Elevator up" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == name).Direction);
        }

        foreach (string name in new[] { "Elevator at bottom", "Elevator at top" })
        {
            Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == name).Direction);
        }

        Assert.Equal(3.0, Catalog.Get("elevator").Param("travel", 0), 3);
        Assert.Equal(0.5, Catalog.Get("elevator").Param("base", 0), 3);
    }

    [Fact]
    public void The_pallet_emitter_names_the_item_it_emits()
    {
        CatalogAsset emitter = Catalog.Get("emitter_pallet");

        Assert.Equal(AssetBehaviours.Emitter, emitter.Behaviour);
        Assert.Equal("pallet", emitter.Emits);
        Assert.Equal(AssetBehaviours.Item, Catalog.Get(emitter.Emits!).Behaviour);
        Assert.Equal(0.5, emitter.OriginHeight, 3);
        Assert.Null(Catalog.Get("emitter_gantry").Emits);

        var scene = new Scene();
        SceneItem item = scene.Add(emitter, 0, 0);
        TagMapEntry tag = Assert.Single(SceneTags.Build(scene, Catalog), entry => entry.Name == item.Name);
        Assert.Equal(TagDirection.Actuator, tag.Direction);
    }

    [Fact]
    public void A_palletizer_gives_six_actuator_tags_and_eight_sensors()
    {
        var scene = new Scene();
        SceneItem station = scene.Add(Catalog.Get("palletizer"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Palletizer", station.Name);
        Assert.Equal(AssetBehaviours.Palletizer, Catalog.Get("palletizer").Behaviour);
        foreach (string suffix in new[] { "feed", "push", "open", "up", "down", "rollers" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Palletizer " + suffix).Direction);
        }

        foreach (string suffix in new[] { "row full", "pusher back", "pusher front", "plate open", "plate closed",
                     "stack high", "table down", "pallet" })
        {
            Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == "Palletizer " + suffix).Direction);
        }

        Assert.Equal(14, tags.Count(tag => tag.Name.StartsWith("Palletizer ", StringComparison.Ordinal)));
        Assert.Equal(1.9, Catalog.Get("palletizer").Param("deck", 0), 3);
        Assert.Contains("Leaf_A", Catalog.Get("palletizer").Parts);
        Assert.Contains("Leaf_B", Catalog.Get("palletizer").Parts);
    }

    [Fact]
    public void A_machining_center_gives_two_actuator_tags_and_three_sensors()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("machining_center");
        SceneItem station = scene.Add(asset, 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal("Machining center", station.Name);
        Assert.Equal(AssetBehaviours.Machining, asset.Behaviour);
        foreach (string suffix in new[] { "start", "lid" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Machining center " + suffix).Direction);
        }

        foreach (string suffix in new[] { "busy", "entry", "exit" })
        {
            Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == "Machining center " + suffix).Direction);
        }

        Assert.Equal(5, tags.Count(tag => tag.Name.StartsWith("Machining center ", StringComparison.Ordinal)));
        Assert.Contains("RobotArm", asset.Parts);
        Assert.Contains("Spindle", asset.Parts);

        // Pick position, machine table and drop position lie on one circle around the robot.
        double reach = asset.Param("reach", 0);
        double dx = asset.Param("pickX", 0) - asset.Param("baseX", 0);
        Assert.Equal(reach, Math.Sqrt(dx * dx + Math.Pow(asset.Param("lane", 0), 2)), 3);
        double angle = 180.0 - Math.Atan2(asset.Param("lane", 0), -dx) * 180.0 / Math.PI;
        Assert.InRange(Math.Abs(angle - asset.Param("entryAngle", 0)), 0.0, 0.01);
    }

    [Fact]
    public void Raw_material_has_an_emitter_per_colour_and_every_product_it_can_become()
    {
        foreach (string colour in new[] { "blue", "green", "metal" })
        {
            CatalogAsset emitter = Catalog.Get("emitter_raw_" + colour);
            Assert.Equal(AssetBehaviours.Emitter, emitter.Behaviour);
            Assert.Equal("raw_material_" + colour, emitter.Emits);
            Assert.NotNull(Catalog.Find(MachiningCycle.Product(emitter.Emits, lid: false)!));
            Assert.NotNull(Catalog.Find(MachiningCycle.Product(emitter.Emits, lid: true)!));
        }
    }

    [Fact]
    public void Items_are_centred_on_their_origin_like_their_collider()
    {
        foreach (CatalogAsset item in Catalog.Assets.Where(asset => asset.Behaviour == AssetBehaviours.Item))
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.True(Math.Abs(item.BoundsMin[axis] + item.BoundsMax[axis]) < 0.011, $"{item.Id} axis {axis}");
            }
        }
    }

    [Fact]
    public void An_arm_sorter_gives_one_actuator_tag()
    {
        var scene = new Scene();
        SceneItem sorter = scene.Add(Catalog.Get("pivot_arm_sorter"), 0, 0);

        TagMapEntry tag = Assert.Single(SceneTags.Build(scene, Catalog), t => t.Name.StartsWith("Arm sorter"));

        Assert.Equal("Arm sorter", sorter.Name);
        Assert.Equal(AssetBehaviours.ArmSorter, Catalog.Get("pivot_arm_sorter").Behaviour);
        Assert.Equal(TagDirection.Actuator, tag.Direction);
    }

    [Fact]
    public void Sorters_carry_the_dimensions_the_simulator_needs()
    {
        CatalogAsset arm = Catalog.Get("pivot_arm_sorter");
        CatalogAsset wheels = Catalog.Get("popup_wheel_sorter");

        Assert.Equal(1.2, arm.Param("length", 0), 3);
        Assert.Equal(45.0, arm.Param("angle", 0), 3);
        Assert.Equal(1.0, wheels.Param("length", 0), 3);
        Assert.Equal(45.0, wheels.Param("angle", 0), 3);
    }

    [Fact]
    public void Stop_and_curve_carry_the_dimensions_the_simulator_needs()
    {
        CatalogAsset stop = Catalog.Get("stop_blade");
        CatalogAsset curve = Catalog.Get("belt_conveyor_curved");

        Assert.Equal(0.12, stop.Param("stroke", 0), 3);
        Assert.Equal(0.72, stop.Param("bladeWidth", 0), 3);
        Assert.Equal(1.0, curve.Param("radius", 0), 3);
        Assert.Equal(0.6, curve.Param("speed", 0), 3);
    }

    [Fact]
    public void Always_on_conveyor_has_no_tag()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.SetParam(belt.Id, SceneTags.AlwaysOn, 1);

        Assert.DoesNotContain(SceneTags.Build(scene, Catalog), tag => tag.Name == "Conveyor");
    }

    [Fact]
    public void Existing_addresses_survive_when_items_are_added_or_removed()
    {
        var scene = new Scene();
        SceneItem first = scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);
        SceneItem second = scene.Add(Catalog.Get("diffuse_sensor"), 2, 0);
        scene.Tags.AddRange(SceneTags.Build(scene, Catalog));
        int secondOffset = scene.Tags.Single(tag => tag.Name == second.Name).Offset;

        scene.Remove(first.Id);
        scene.Add(Catalog.Get("pusher"), 4, 0);
        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(secondOffset, tags.Single(tag => tag.Name == second.Name).Offset);
        // The freed offset is reused by one of the new tags; nothing overlaps.
        _ = new TagMap(tags);
        Assert.DoesNotContain(tags, tag => tag.Name == first.Name);
    }

    [Fact]
    public void Clashing_tag_names_are_a_problem_not_a_crash_in_the_editor()
    {
        var scene = new Scene();
        SceneItem pusher = scene.Add(Catalog.Get("pusher"), 0, 0);
        SceneItem sensor = scene.Add(Catalog.Get("diffuse_sensor"), 2, 0);
        scene.Rename(sensor.Id, SceneTags.PusherBack(pusher.Name));

        Assert.Single(SceneTags.Problems(scene, Catalog));
        Assert.Throws<SceneException>(() => SceneTags.Build(scene, Catalog));
    }

    [Fact]
    public void Unknown_asset_is_reported_as_a_problem()
    {
        Scene scene = Scene.FromJson(
            "{ \"format\": \"factorysim-level\", \"version\": 1, \"items\": [ { \"id\": \"a\", \"asset\": \"gone\", \"name\": \"Oud\", \"position\": [0,0,0] } ] }");

        Assert.Contains("gone", Assert.Single(SceneTags.Problems(scene, Catalog)));
        Assert.DoesNotContain(SceneTags.Build(scene, Catalog), tag => tag.Name == "Oud");
    }

    // ---- the built-in scene

    [Fact]
    public void Mvp_scene_uses_only_library_assets_and_has_no_problems()
    {
        Scene scene = LoadMvp();

        Assert.Equal(14, scene.Items.Count);
        Assert.All(scene.Items, item => Assert.NotNull(Catalog.Find(item.Asset)));
        Assert.Empty(SceneTags.Problems(scene, Catalog));
    }

    [Fact]
    public void Mvp_scene_keeps_the_tag_addresses_of_the_plc_project()
    {
        TagMap reference = TagMap.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tagmap.json")));

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(LoadMvp(), Catalog);

        Assert.Equal(
            reference.Entries.Select(tag => (tag.Name, tag.Area, tag.Offset)).OrderBy(tag => tag),
            tags.Select(tag => (tag.Name, tag.Area, tag.Offset)).OrderBy(tag => tag));
    }

    // ---- stacker crane and rack

    private static readonly string[] CraneShared =
    {
        "forks left", "forks right", "lift", "forks middle", "forks left end", "forks right end", "lifted", "load",
    };

    [Fact]
    public void A_stacker_crane_in_target_mode_gives_forks_lift_and_target_tags()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("stacker_crane");
        SceneItem crane = scene.Add(asset, 0, 0);

        IReadOnlyList<TagDefinition> tags = SceneTags.TagsOf(crane, asset).ToArray();

        Assert.Equal("Stacker crane", crane.Name);
        Assert.Equal(AssetBehaviours.StackerCrane, asset.Behaviour);
        string[] actuators = { "forks left", "forks right", "lift", "target 1", "target 2", "target 4", "target 8", "go" };
        string[] sensors = { "forks middle", "forks left end", "forks right end", "lifted", "load", "busy", "at target", "error" };
        Assert.Equal(actuators.Concat(sensors).Select(suffix => "Stacker crane " + suffix).OrderBy(name => name),
            tags.Select(tag => tag.Name).OrderBy(name => name));
        Assert.All(actuators, suffix => Assert.Equal(TagDirection.Actuator,
            tags.Single(tag => tag.Name == "Stacker crane " + suffix).Direction));
        Assert.All(sensors, suffix => Assert.Equal(TagDirection.Sensor,
            tags.Single(tag => tag.Name == "Stacker crane " + suffix).Direction));
    }

    [Fact]
    public void A_stacker_crane_in_jog_mode_gives_directions_and_position_sensors_instead()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("stacker_crane");
        SceneItem crane = scene.Add(asset, 0, 0);
        crane = scene.SetParam(crane.Id, SceneTags.StackerJog, 1);

        IReadOnlyList<TagDefinition> tags = SceneTags.TagsOf(crane, asset).ToArray();

        string[] actuators = { "left", "right", "up", "down" };
        string[] sensors = { "at column 1", "at column 2", "at column 3", "at level 0", "at level 1", "at level 2", "at level 3", "at level 4" };
        string[] shared = CraneShared;
        Assert.Equal(actuators.Concat(sensors).Concat(shared).Select(suffix => "Stacker crane " + suffix).OrderBy(name => name),
            tags.Select(tag => tag.Name).OrderBy(name => name));
        Assert.All(actuators, suffix => Assert.Equal(TagDirection.Actuator,
            tags.Single(tag => tag.Name == "Stacker crane " + suffix).Direction));
        Assert.DoesNotContain(tags, tag => tag.Name.EndsWith("go", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rack_is_a_working_asset_without_tags()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("rack");
        SceneItem rack = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Rack, asset.Behaviour);
        Assert.Empty(SceneTags.TagsOf(rack, asset));
        Assert.Equal("Rack", rack.Name);
    }

    [Fact]
    public void The_stacker_crane_carries_a_layout_the_core_accepts_and_a_pallet_fits_in_it()
    {
        CatalogAsset crane = Catalog.Get("stacker_crane");
        CatalogAsset rack = Catalog.Get("rack");
        var positions = new StackerPositions(crane.Param("columnSpacing", 0), crane.Param("levelHeight", 0),
            crane.Param("firstLevelTop", 0), crane.Param("transferTop", 0), crane.Param("clearance", 0));

        Assert.Contains("Cart", crane.Parts);
        Assert.Contains("Platform", crane.Parts);
        Assert.Contains("Forks", crane.Parts);
        Assert.Equal(1.5, crane.Param("columnSpacing", 0), 6);
        Assert.Equal(crane.Param("columnSpacing", 0), rack.Param("columnSpacing", -1), 6);
        Assert.Equal(crane.Param("levelHeight", 0), rack.Param("levelHeight", -1), 6);
        Assert.Equal(crane.Param("firstLevelTop", 0), rack.Param("firstLevelTop", -1), 6);

        // A pallet of 1.2 x 0.8 m, long side across the aisle, passes between the masts and rests on two beams.
        Assert.True(crane.Param("mastGap", 0) >= 0.8 + 0.1);
        Assert.True(crane.Param("forkSpacing", 0) < 0.8 - 0.2);
        Assert.True(crane.Param("platformDepth", 0) >= 1.3);
        Assert.True(rack.Param("beamSpacing", 0) > crane.Param("forkSpacing", 0) + 0.1);
        Assert.True(rack.Param("beamSpacing", 0) < 0.8 - 0.1);
        Assert.Equal(crane.Param("rackOffset", 0), crane.Param("forkStroke", 0), 6);

        // The forks run under the pallet: the lowered forks stay clear of the supports, the raised ones lift it off.
        Assert.True(crane.Param("liftStroke", 0) > crane.Param("clearance", 0) + 0.02);
        Assert.True(positions.MaxHeight > positions.LevelHeight(StackerPositions.Levels));
    }
}
