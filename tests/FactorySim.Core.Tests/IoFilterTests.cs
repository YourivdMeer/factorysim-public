using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 05, part 5: the tags of one part, for "show me the tags of what I clicked".</summary>
public class IoFilterTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static IReadOnlyList<IoPoint> Points(Scene scene) => IoList.Build(scene, Catalog, SceneTags.Build(scene, Catalog));

    [Fact]
    public void The_filter_keeps_exactly_the_tags_of_the_part_and_nothing_of_the_simulator()
    {
        var scene = new Scene();
        SceneItem pusher = scene.Add(Catalog.Get("pusher"), 0, 0);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 2, 0);

        IoPoint[] mine = IoList.ForItem(Points(scene), pusher.Id).ToArray();

        Assert.Equal(new[] { "Pusher", "Pusher back", "Pusher front" }, mine.Select(point => point.Tag.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(mine, point => Assert.Equal(pusher.Id, point.ItemId));
        Assert.DoesNotContain(mine, point => point.ItemId is null);
    }

    [Fact]
    public void No_part_means_all_tags_and_an_unknown_part_means_none()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        IReadOnlyList<IoPoint> points = Points(scene);

        Assert.Equal(points.Count, IoList.ForItem(points, null).Count());
        Assert.Empty(IoList.ForItem(points, "no-such-part"));
    }

    [Fact]
    public void A_renamed_part_keeps_its_filter_and_its_tags_follow_the_new_name()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Rename(belt.Id, "Infeed");

        IoPoint point = Assert.Single(IoList.ForItem(Points(scene), belt.Id));

        Assert.Equal("Infeed", point.Tag.Name);
        Assert.Equal("Infeed", point.Part);
    }

    [Fact]
    public void A_duplicated_part_has_tags_of_its_own()
    {
        var scene = new Scene();
        SceneItem original = scene.Add(Catalog.Get("pusher"), 0, 0);
        SceneItem copy = scene.Duplicate(original.Id, 3, 0);
        IReadOnlyList<IoPoint> points = Points(scene);

        string[] first = IoList.ForItem(points, original.Id).Select(point => point.Tag.Name).ToArray();
        string[] second = IoList.ForItem(points, copy.Id).Select(point => point.Tag.Name).ToArray();

        Assert.Equal(3, first.Length);
        Assert.Equal(3, second.Length);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void The_filter_leaves_the_search_and_the_direction_filter_to_work_on_what_is_left()
    {
        var scene = new Scene();
        SceneItem pusher = scene.Add(Catalog.Get("pusher"), 0, 0);
        IoPoint[] mine = IoList.ForItem(Points(scene), pusher.Id).ToArray();

        string[] sensors = mine.Where(point => IoList.Matches(point, TagFilter.Sensors, null, forced: false)).Select(point => point.Tag.Name).ToArray();

        Assert.Equal(new[] { "Pusher back", "Pusher front" }, sensors);
    }
}
