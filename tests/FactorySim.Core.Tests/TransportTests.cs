using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 02: the small transport parts that used to be decoration.</summary>
public class TransportTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    // ---- A. spur, scale and gate are belts

    [Theory]
    [InlineData("spur_conveyor", 1.6)]
    [InlineData("conveyor_scale", 1.0)]
    [InlineData("belt_conveyor_gate", 2.0)]
    public void The_spur_scale_and_gate_conveyors_are_belt_conveyors_with_one_actuator_tag(string id, double length)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Conveyor, asset.Behaviour);
        Assert.Equal(length, asset.Param("length", 0.0));
        Assert.Equal(0.8, asset.Param("width", 0.0));
        Assert.True(asset.Param("speed", 0.0) > 0.0);
        Assert.Equal(0.8, asset.OriginHeight);
        Assert.All(new[] { "Belt", "Roller_Head", "Roller_Tail" }, part => Assert.Contains(part, asset.Parts));

        // The weighing conveyor also has an Int weight tag (plan 06); see AnalogPartTagsTests.
        TagDefinition tag = Assert.Single(SceneTags.TagsOf(item, asset), t => t.Kind == TagKind.Bool);
        Assert.Equal(TagDirection.Actuator, tag.Direction);
        Assert.Equal(item.Name, tag.Name);
        Assert.Equal(TagDirection.Actuator, SceneTags.Build(scene, Catalog).Single(entry => entry.Name == item.Name).Direction);
    }

    [Theory]
    [InlineData("spur_conveyor")]
    [InlineData("conveyor_scale")]
    [InlineData("belt_conveyor_gate")]
    public void The_spur_scale_and_gate_conveyors_take_part_in_supports_and_overlap(string id)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem first = scene.Add(asset, 0, 0);
        SceneItem second = scene.Add(asset, 0.25, 0);

        Assert.Empty(SceneSupports.For(first, asset));                    // stands on the floor
        SceneItem raised = scene.SetHeight(first.Id, 1.9);
        Assert.NotEmpty(SceneSupports.For(raised, asset));                // a raised belt gets a frame
        scene.SetHeight(first.Id, 0.8);

        (SceneItem First, SceneItem Second) pair = Assert.Single(SceneOverlap.Find(scene, Catalog));
        Assert.Equal((first.Id, second.Id), (pair.First.Id, pair.Second.Id));
    }

    // ---- B. inclined belt

    [Fact]
    public void The_inclined_belt_is_a_belt_conveyor_that_climbs_one_metre_over_four()
    {
        CatalogAsset asset = Catalog.Get("belt_conveyor_inclined");
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Conveyor, asset.Behaviour);
        Assert.Equal(4.0, asset.Param("length", 0.0));
        Assert.Equal(1.0, asset.Param("rise", 0.0));
        Assert.Equal(0.8, asset.Param("width", 0.0));
        Assert.True(asset.Param("speed", 0.0) > 0.0);
        // The origin is halfway up the slope: the low end meets a belt at 0.8 m, the high end one at 1.8 m.
        Assert.Equal(1.3, asset.OriginHeight, 6);
        Assert.All(new[] { "Belt", "Roller_Head", "Roller_Tail" }, part => Assert.Contains(part, asset.Parts));

        TagDefinition tag = Assert.Single(SceneTags.TagsOf(item, asset));
        Assert.Equal((item.Name, TagDirection.Actuator), (tag.Name, tag.Direction));
    }

    [Fact]
    public void The_inclined_belt_stands_on_the_floor_with_its_own_legs_and_gets_a_frame_when_raised()
    {
        CatalogAsset asset = Catalog.Get("belt_conveyor_inclined");
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Empty(SceneSupports.For(item, asset));
        Assert.NotEmpty(SceneSupports.For(scene.SetHeight(item.Id, 2.3), asset));
    }

    [Fact]
    public void The_inclined_belt_meets_a_belt_at_each_end_without_overlapping_them()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);                       // x -1..1 at 0.8 m
        scene.Add(Catalog.Get("belt_conveyor_inclined"), 3, 0);                 // x 1..5, from 0.8 m up to 1.8 m
        SceneItem upper = scene.Add(Catalog.Get("belt_conveyor_2m"), 6, 0);     // x 5..7
        scene.SetHeight(upper.Id, 1.8);

        Assert.Empty(SceneOverlap.Find(scene, Catalog));
    }

    // ---- D. positioning bars

    [Fact]
    public void The_positioning_bars_give_one_actuator_and_two_sensors_and_a_speed_setting()
    {
        CatalogAsset asset = Catalog.Get("positioning_bars");
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Clamp, asset.Behaviour);
        Assert.Equal("Clamp", item.Name);
        Assert.Equal(
            new[] { ("Clamp", TagDirection.Actuator), ("Clamp open", TagDirection.Sensor), ("Clamp closed", TagDirection.Sensor) },
            SceneTags.TagsOf(item, asset).Select(tag => (tag.Name, tag.Direction)));
        Assert.All(new[] { "Clamp_Left", "Clamp_Right" }, part => Assert.Contains(part, asset.Parts));
        Assert.Contains(ItemParams.Editable(asset), field => field.Name == "speed");
    }

    [Fact]
    public void The_positioning_bars_catalogue_numbers_make_a_valid_clamp_that_takes_the_standard_box()
    {
        CatalogAsset asset = Catalog.Get("positioning_bars");
        double openHalfGap = asset.Param("openHalfGap", 0.0);
        double stroke = asset.Param("stroke", 0.0);

        var clamp = new PositioningClamp(openHalfGap, stroke, asset.Param("speed", 0.0));    // throws on nonsense

        Assert.True(openHalfGap - stroke > 0.0);
        Assert.InRange(openHalfGap, 0.45, 0.6);                                              // a 0.30 m box on an 0.8 m belt fits
        Assert.True(openHalfGap - stroke < 0.15);                                            // and the blades can close on a 0.30 m box
        Assert.True(clamp.Open);
    }

    [Fact]
    public void Only_a_belt_that_climbs_offers_to_run_downhill()
    {
        ParamField reverse = ItemParams.Editable(Catalog.Get("belt_conveyor_inclined")).Single(field => field.Name == SceneTags.Reverse);

        Assert.True(reverse.IsToggle);
        Assert.Equal(0.0, reverse.Default);
        Assert.DoesNotContain(ItemParams.Editable(Catalog.Get("belt_conveyor_2m")), field => field.Name == SceneTags.Reverse);
        Assert.Contains(ItemParams.Editable(Catalog.Get("belt_conveyor_inclined")), field => field.Name == SceneTags.AlwaysOn);
    }
}
