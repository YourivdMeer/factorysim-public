using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 03: the other digital sensors, the encoder and the safety door.</summary>
public class SensorTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    // ---- metal rule

    [Theory]
    [InlineData("raw_material_metal", true)]
    [InlineData("product_base_metal", true)]
    [InlineData("final_product_metal", true)]
    [InlineData("raw_material_blue", false)]
    [InlineData("pallet", false)]
    [InlineData("metal_corner", false)]
    [InlineData(null, false)]
    public void An_item_is_metal_when_its_id_ends_in_metal(string? id, bool expected)
    {
        Assert.Equal(expected, ItemKinds.IsMetal(id));
    }

    // ---- encoder

    [Fact]
    public void An_encoder_at_rest_shows_A_high_and_B_low()
    {
        var encoder = new EncoderLogic(0.05);

        Assert.True(encoder.A);
        Assert.False(encoder.B);
        Assert.Equal(0.0, encoder.Distance);
    }

    [Fact]
    public void Moving_forward_runs_through_the_four_states_with_B_a_quarter_period_behind_A()
    {
        var encoder = new EncoderLogic(0.05);
        var states = new List<(bool, bool)>();

        // Sample in the middle of each quarter of a period.
        for (int quarter = 0; quarter < 4; quarter++)
        {
            encoder.Reset();
            encoder.Advance(0.05 * (quarter + 0.5) / 4.0);
            states.Add((encoder.A, encoder.B));
        }

        Assert.Equal(new[] { (true, false), (true, true), (false, true), (false, false) }, states);
    }

    [Fact]
    public void Moving_backwards_runs_through_the_same_states_the_other_way_round()
    {
        var encoder = new EncoderLogic(0.05);
        var states = new List<(bool, bool)>();

        for (int quarter = 1; quarter <= 4; quarter++)
        {
            encoder.Reset();
            encoder.Advance(-0.05 * (quarter - 0.5) / 4.0);
            states.Add((encoder.A, encoder.B));
        }

        Assert.Equal(new[] { (false, false), (false, true), (true, true), (true, false) }, states);
    }

    [Fact]
    public void Ten_pulse_distances_give_ten_rising_edges_of_A_and_standing_still_gives_none()
    {
        var encoder = new EncoderLogic(0.05);
        int edges = 0;
        bool before = encoder.A;
        for (int i = 0; i < 5100; i++)
        {
            encoder.Advance(0.0001);                  // 0.51 m in steps of 0.1 mm: ten periods and a bit
            edges += !before && encoder.A ? 1 : 0;
            before = encoder.A;
        }

        Assert.Equal(10, edges);

        for (int i = 0; i < 100; i++)
        {
            encoder.Advance(0.0);
            edges += !before && encoder.A ? 1 : 0;
        }

        Assert.Equal(10, edges);
        Assert.Equal(0.51, encoder.Distance, 6);
    }

    [Fact]
    public void An_encoder_needs_a_positive_pulse_distance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncoderLogic(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncoderLogic(-0.1));
    }

    // ---- pairing

    private static (Scene Scene, SceneItem Sensor) Placed(string sensorId, double sensorYaw, params (string Asset, double X, double Z, double Yaw)[] others)
    {
        var scene = new Scene();
        SceneItem sensor = scene.Add(Catalog.Get(sensorId), 0, 0, sensorYaw);
        foreach ((string asset, double x, double z, double yaw) in others)
        {
            scene.Add(Catalog.Get(asset), x, z, yaw);
        }

        return (scene, sensor);
    }

    [Fact]
    public void A_reflector_straight_ahead_within_range_is_paired_with_its_distance()
    {
        // Yaw 180: the sensor looks towards -Z.
        (Scene scene, SceneItem sensor) = Placed("retroreflective_sensor", 180, ("reflector", 0, -1.5, 0));

        SensorLink link = Assert.Single(SensorPairing.Links(scene, Catalog));

        Assert.Equal(sensor.Id, link.SensorId);
        Assert.Equal(scene.Items.Single(item => item.Asset == "reflector").Id, link.TargetId);
        Assert.Equal(1.5, link.Distance, 6);
        Assert.Empty(SensorPairing.Unpaired(scene, Catalog));
    }

    [Theory]
    [InlineData(0.0, 0.0, 1.5)]       // behind the sensor (it looks towards -Z)
    [InlineData(0.0, 0.0, -7.0)]      // beyond the 6 m of range
    [InlineData(1.0, 0.0, -2.75)]     // 20 degrees off the line of sight
    [InlineData(0.0, 1.0, -2.0)]      // a metre above it
    public void A_reflector_behind_out_of_range_off_axis_or_at_another_height_is_not_paired(double x, double height, double z)
    {
        var scene = new Scene();
        SceneItem sensor = scene.Add(Catalog.Get("retroreflective_sensor"), 0, 0, 180);
        SceneItem reflector = scene.Add(Catalog.Get("reflector"), x, z);
        if (height != 0.0)
        {
            scene.SetHeight(reflector.Id, reflector.Y + height);
        }

        Assert.Empty(SensorPairing.Links(scene, Catalog));
        Assert.Equal(sensor.Id, Assert.Single(SensorPairing.Unpaired(scene, Catalog)).Id);
    }

    [Fact]
    public void The_nearest_of_two_reflectors_in_line_is_paired_and_a_turned_sensor_looks_the_turned_way()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("retroreflective_sensor"), 0, 0, 90);             // yaw 90: looks towards +X
        SceneItem far = scene.Add(Catalog.Get("reflector"), 4, 0);
        SceneItem near = scene.Add(Catalog.Get("reflector"), 2, 0);
        scene.Add(Catalog.Get("reflector"), -2, 0);                            // behind it

        SensorLink link = Assert.Single(SensorPairing.Links(scene, Catalog));

        Assert.Equal(near.Id, link.TargetId);
        Assert.NotEqual(far.Id, link.TargetId);
        Assert.Equal(2.0, link.Distance, 6);
    }

    [Fact]
    public void A_light_array_receiver_pairs_only_with_a_light_array_emitter_and_a_reflection_sensor_only_with_a_reflector()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("light_array_receiver"), 0, 0, 180);
        scene.Add(Catalog.Get("retroreflective_sensor"), 5, 0, 180);
        scene.Add(Catalog.Get("reflector"), 0, -1.5);                           // in front of the receiver: no use to it
        SceneItem emitter = scene.Add(Catalog.Get("light_array_emitter"), 5, -1.5);   // in front of the sensor: no use either

        Assert.Empty(SensorPairing.Links(scene, Catalog));
        Assert.Equal(2, SensorPairing.Unpaired(scene, Catalog).Count);

        scene.Add(Catalog.Get("light_array_emitter"), 0, -2.0);
        scene.Add(Catalog.Get("reflector"), 5, -2.0);

        Assert.Equal(2, SensorPairing.Links(scene, Catalog).Count);
        Assert.Empty(SensorPairing.Unpaired(scene, Catalog));
        Assert.NotNull(emitter);
    }

    [Fact]
    public void The_diffuse_sensor_needs_no_partner()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);

        Assert.Empty(SensorPairing.Links(scene, Catalog));
        Assert.Empty(SensorPairing.Unpaired(scene, Catalog));
    }

    // ---- the assets

    [Theory]
    [InlineData("capacitive_sensor", "Capacitive sensor", 0.2, false)]
    [InlineData("inductive_sensor", "Inductive sensor", 0.1, true)]
    public void The_capacitive_and_inductive_sensors_are_short_range_photo_eyes_and_only_the_second_wants_metal(string id, string name, double range, bool metalOnly)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.PhotoEye, asset.Behaviour);
        Assert.Equal(name, item.Name);
        Assert.Equal(range, asset.Param("range", 0.0));
        Assert.Equal(metalOnly, asset.Param("metalOnly", 0.0) > 0.5);
        TagDefinition tag = Assert.Single(SceneTags.TagsOf(item, asset));
        Assert.Equal((name, TagDirection.Sensor), (tag.Name, tag.Direction));
        Assert.Contains(ItemParams.Editable(asset), field => field.Name == "range");
        Assert.Contains(ItemParams.Editable(asset), field => field.Name == "post");
    }

    [Theory]
    [InlineData("retroreflective_sensor", "Retroreflective sensor", 6.0)]
    [InlineData("light_array_receiver", "Light array", 3.0)]
    public void The_paired_sensors_give_one_bool_sensor_tag_and_offer_range_and_post(string id, string name, double range)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        // A light array also has an Int tag with the number of interrupted beams (plan 06); see AnalogPartTagsTests.
        TagDefinition tag = Assert.Single(SceneTags.TagsOf(item, asset), t => t.Kind == TagKind.Bool);

        Assert.Equal((name, TagDirection.Sensor), (tag.Name, tag.Direction));
        Assert.Equal(range, asset.Param("range", 0.0));
        string[] fields = ItemParams.Editable(asset).Select(field => field.Name).ToArray();
        Assert.Contains("range", fields);
        Assert.Contains("post", fields);
    }

    [Fact]
    public void The_light_array_receiver_has_beams_inside_its_bar()
    {
        CatalogAsset asset = Catalog.Get("light_array_receiver");
        double beams = asset.Param("beams", 0.0);
        double spacing = asset.Param("beamSpacing", 0.0);

        Assert.True(beams >= 2.0);
        Assert.True((beams - 1.0) * spacing < asset.Size[1]);           // the bar is as tall as the outermost beams are apart, at least
        Assert.True((beams - 1.0) * spacing > 0.25);                    // and the array covers a box of 0.20 m
    }

    [Theory]
    [InlineData("reflector", AssetBehaviours.Reflector, "Reflector")]
    [InlineData("light_array_emitter", AssetBehaviours.LightEmitter, "Light array emitter")]
    public void The_reflector_and_the_light_array_emitter_are_targets_without_tags_or_settings(string id, string behaviour, string name)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(behaviour, asset.Behaviour);
        Assert.Equal(name, item.Name);
        Assert.Empty(SceneTags.TagsOf(item, asset));
        Assert.Empty(ItemParams.Editable(asset));
        Assert.True(asset.IsFunctional);
    }

    [Fact]
    public void The_encoder_gives_two_sensors_and_offers_its_pulse_distance()
    {
        CatalogAsset asset = Catalog.Get("incremental_encoder");
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Encoder, asset.Behaviour);
        Assert.Equal("Encoder", item.Name);
        Assert.Equal(
            new[] { ("Encoder A", TagDirection.Sensor), ("Encoder B", TagDirection.Sensor) },
            SceneTags.TagsOf(item, asset).Select(tag => (tag.Name, tag.Direction)));
        Assert.Equal(0.05, asset.Param("pulseDistance", 0.0));
        Assert.Contains("Wheel", asset.Parts);
        ParamField field = ItemParams.Editable(asset).Single(f => f.Name == "pulseDistance");
        Assert.False(field.IsToggle);
        Assert.Equal(0.05, field.Default);
    }

    [Fact]
    public void The_safety_door_is_a_latching_normally_closed_control_that_swings()
    {
        CatalogAsset asset = Catalog.Get("safety_door");
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Button, asset.Behaviour);
        Assert.Equal("Safety door", item.Name);
        Assert.Equal(1.0, asset.Param("latching", 0.0));
        Assert.Equal(1.0, asset.Param("normallyClosed", 0.0));
        Assert.Equal(1.0, asset.Param("door", 0.0));
        Assert.Equal(0.0, asset.Param("post", 1.0));                    // stands on its own posts
        Assert.Contains("Leaf", asset.Parts);
        TagDefinition tag = Assert.Single(SceneTags.TagsOf(item, asset));
        Assert.Equal(("Safety door", TagDirection.Sensor), (tag.Name, tag.Direction));
    }
}
