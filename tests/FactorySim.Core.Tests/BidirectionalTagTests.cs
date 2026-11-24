using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

/// <summary>Plan 12: the tags of a belt with <c>twoWay</c> and a pusher with <c>bistable</c>.</summary>
public class BidirectionalTagTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static string[] Names(SceneItem item, CatalogAsset asset) =>
        SceneTags.TagsOf(item, asset).Select(tag => tag.Name).ToArray();

    [Fact]
    public void A_belt_without_the_setting_keeps_its_one_tag()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("belt_conveyor_4m");
        SceneItem belt = scene.Add(asset, 0, 0);

        Assert.Equal(new[] { "Conveyor" }, Names(belt, asset));
        Assert.Contains(ItemParams.Editable(asset), field => field.Name == SceneTags.TwoWay && field.IsToggle && field.Default == 0.0);
    }

    [Theory]
    [InlineData("belt_conveyor_4m")]
    [InlineData("roller_conveyor_2m")]
    [InlineData("belt_conveyor_inclined")]
    public void A_two_way_belt_gets_a_reverse_tag_after_its_forward_tag(string assetId)
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get(assetId);
        SceneItem belt = scene.Add(asset, 0, 0);
        belt = scene.SetParam(belt.Id, SceneTags.TwoWay, 1);

        TagDefinition[] tags = SceneTags.TagsOf(belt, asset).ToArray();

        Assert.Equal(new[] { belt.Name, belt.Name + " reverse" }, tags.Select(tag => tag.Name));
        Assert.All(tags, tag => Assert.Equal(TagDirection.Actuator, tag.Direction));
        Assert.Equal(belt.Name + " reverse", SceneTags.ConveyorReverse(belt.Name));
    }

    [Fact]
    public void An_always_on_belt_has_no_tags_even_when_two_way()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("belt_conveyor_4m");
        SceneItem belt = scene.Add(asset, 0, 0);
        scene.SetParam(belt.Id, SceneTags.TwoWay, 1);
        belt = scene.SetParam(belt.Id, SceneTags.AlwaysOn, 1);

        Assert.Empty(Names(belt, asset));
    }

    [Fact]
    public void A_bistable_pusher_gets_a_retract_coil_and_keeps_its_sensors()
    {
        var scene = new Scene();
        CatalogAsset asset = Catalog.Get("pusher");
        SceneItem pusher = scene.Add(asset, 0, 0);
        Assert.Equal(new[] { "Pusher", "Pusher back", "Pusher front" }, Names(pusher, asset));
        Assert.Contains(ItemParams.Editable(asset), field => field.Name == SceneTags.Bistable && field.IsToggle);

        pusher = scene.SetParam(pusher.Id, SceneTags.Bistable, 1);
        TagDefinition[] tags = SceneTags.TagsOf(pusher, asset).ToArray();

        Assert.Equal(new[] { "Pusher", "Pusher retract", "Pusher back", "Pusher front" }, tags.Select(tag => tag.Name));
        Assert.Equal(TagDirection.Actuator, tags[1].Direction);
        Assert.Equal("Pusher retract", SceneTags.PusherRetract("Pusher"));
    }

    [Fact]
    public void Turning_the_settings_on_adds_only_the_second_tag_and_moves_no_other_address()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        SceneItem pusher = scene.Add(Catalog.Get("pusher"), 2, 0);
        scene.Add(Catalog.Get("diffuse_sensor"), 4, 0);
        scene.Add(Catalog.Get("emitter_gantry"), 6, 0);
        scene.Tags.AddRange(SceneTags.Build(scene, Catalog));
        TagMapEntry[] before = scene.Tags.ToArray();

        scene.SetParam(belt.Id, SceneTags.TwoWay, 1);
        scene.SetParam(pusher.Id, SceneTags.Bistable, 1);
        IReadOnlyList<TagMapEntry> after = SceneTags.Build(scene, Catalog);

        Assert.All(before, tag => Assert.Contains(tag, after));
        string[] added = after.Except(before).Select(tag => tag.Name).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "Conveyor reverse", "Pusher retract" }, added);
        Assert.Equal(after.Count, after.Select(tag => (tag.Area, tag.Offset)).Distinct().Count());
    }

    [Fact]
    public void The_mvp_scene_gives_the_same_tags_as_before()
    {
        Scene scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(scene.Tags.OrderBy(tag => tag.Name), tags.OrderBy(tag => tag.Name));
    }
}
