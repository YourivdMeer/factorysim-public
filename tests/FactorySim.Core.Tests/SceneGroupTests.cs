using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 05: selecting and changing several items at once, the floor size and the overlap markers.</summary>
public class SceneGroupTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static (Scene Scene, string[] Ids) Line()
    {
        var scene = new Scene();
        SceneItem a = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        SceneItem b = scene.Add(Catalog.Get("belt_conveyor_2m"), 2, 0);
        SceneItem c = scene.Add(Catalog.Get("remover_hood_1m0"), 3.25, 0);
        scene.Add(Catalog.Get("belt_conveyor_4m"), 0, 5);              // not part of the group
        return (scene, new[] { a.Id, b.Id, c.Id });
    }

    // ---- group operations

    [Fact]
    public void Moving_a_group_shifts_every_member_by_the_same_amount_and_leaves_the_others()
    {
        (Scene scene, string[] ids) = Line();

        scene.MoveMany(ids, 1.5, -0.75);

        Assert.Equal((1.5, -0.75), (scene.Find(ids[0])!.X, scene.Find(ids[0])!.Z));
        Assert.Equal((3.5, -0.75), (scene.Find(ids[1])!.X, scene.Find(ids[1])!.Z));
        Assert.Equal((4.75, -0.75), (scene.Find(ids[2])!.X, scene.Find(ids[2])!.Z));
        Assert.Equal((0.0, 5.0), (scene.Items[3].X, scene.Items[3].Z));
    }

    [Fact]
    public void The_centre_of_a_group_is_the_middle_of_its_extent_on_the_grid()
    {
        (Scene scene, string[] ids) = Line();

        (double x, double z) = scene.Centre(ids);

        Assert.Equal((1.5, 0.0), (x, z));                   // x runs from 0 to 3.25: the middle is 1.625, on the grid 1.5
    }

    [Fact]
    public void Turning_a_group_a_quarter_turn_turns_the_layout_with_it_and_turning_back_restores_it()
    {
        (Scene scene, string[] ids) = Line();
        (double cx, double cz) = scene.Centre(ids);
        (double X, double Z, double Yaw)[] before = ids.Select(id => (scene.Find(id)!.X, scene.Find(id)!.Z, scene.Find(id)!.Yaw)).ToArray();

        scene.RotateMany(ids, 90.0, cx, cz);

        // A positive turn takes +X towards -Z: the item that lay 1.5 m left of the centre now lies 1.5 m in +Z.
        SceneItem first = scene.Find(ids[0])!;
        Assert.Equal(cx, first.X, 6);
        Assert.Equal(cz + 1.5, first.Z, 6);
        Assert.All(ids, id => Assert.Equal(90.0, scene.Find(id)!.Yaw));
        // The distances between the members did not change.
        Assert.Equal(2.0, Math.Abs(scene.Find(ids[1])!.Z - first.Z), 6);

        scene.RotateMany(ids, -90.0, cx, cz);

        (double X, double Z, double Yaw)[] after = ids.Select(id => (scene.Find(id)!.X, scene.Find(id)!.Z, scene.Find(id)!.Yaw)).ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Four_quarter_turns_in_a_row_come_back_to_where_they_started()
    {
        (Scene scene, string[] ids) = Line();
        (double cx, double cz) = scene.Centre(ids);
        (double X, double Z, double Yaw)[] before = ids.Select(id => (scene.Find(id)!.X, scene.Find(id)!.Z, scene.Find(id)!.Yaw)).ToArray();

        for (int i = 0; i < 4; i++)
        {
            scene.RotateMany(ids, 90.0, cx, cz);
        }

        Assert.Equal(before, ids.Select(id => (scene.Find(id)!.X, scene.Find(id)!.Z, scene.Find(id)!.Yaw)).ToArray());
    }

    [Fact]
    public void Duplicating_a_group_keeps_the_layout_and_gives_every_copy_a_name_of_its_own()
    {
        (Scene scene, string[] ids) = Line();
        string[] names = ids.Select(id => scene.Find(id)!.Name).ToArray();

        IReadOnlyList<SceneItem> copies = scene.DuplicateMany(ids, 0.0, 2.0);

        Assert.Equal(3, copies.Count);
        Assert.Equal(7, scene.Items.Count);
        Assert.Equal(new[] { 0.0, 2.0, 3.25 }, copies.Select(copy => copy.X));
        Assert.All(copies, copy => Assert.Equal(2.0, copy.Z));
        Assert.DoesNotContain(copies, copy => names.Contains(copy.Name));
        Assert.Equal(scene.Items.Count, scene.Items.Select(item => item.Name).Distinct().Count());
        Assert.Equal(scene.Items.Count, scene.Items.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void Removing_a_group_takes_exactly_its_members_away()
    {
        (Scene scene, string[] ids) = Line();

        int removed = scene.RemoveMany(ids.Take(2));

        Assert.Equal(2, removed);
        Assert.Equal(2, scene.Items.Count);
        Assert.Null(scene.Find(ids[0]));
        Assert.NotNull(scene.Find(ids[2]));
    }

    [Fact]
    public void Pasting_a_group_puts_its_middle_at_the_chosen_spot()
    {
        (Scene scene, string[] ids) = Line();
        SceneItem[] sources = ids.Select(id => scene.Find(id)!).ToArray();

        IReadOnlyList<SceneItem> pasted = scene.PasteMany(sources, 5.0, 8.0);

        (double cx, double cz) = (1.5, 0.0);                 // centre of the group
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(Scene.Snap(sources[i].X + (5.0 - cx)), pasted[i].X, 6);
            Assert.Equal(Scene.Snap(sources[i].Z + (8.0 - cz)), pasted[i].Z, 6);
        }
    }

    [Fact]
    public void A_group_operation_on_an_unknown_id_is_ignored_and_one_on_nothing_changes_nothing()
    {
        (Scene scene, string[] ids) = Line();
        string before = scene.ToJson();

        scene.MoveMany(Array.Empty<string>(), 1, 1);
        scene.RotateMany(Array.Empty<string>(), 90, 0, 0);
        Assert.Equal(0, scene.RemoveMany(new[] { "no-such-id" }));
        Assert.Empty(scene.DuplicateMany(Array.Empty<string>(), 1, 1));

        Assert.Equal(before, scene.ToJson());
        Assert.Equal(3, ids.Length);
    }

    // ---- selecting with a frame

    [Fact]
    public void A_frame_on_the_floor_selects_the_items_that_stand_in_it()
    {
        (Scene scene, string[] ids) = Line();

        string[] inFrame = ScenePicking.ItemsIn(scene, Catalog, -1.5, -1.0, 4.0, 1.0).Select(item => item.Id).ToArray();
        string[] none = ScenePicking.ItemsIn(scene, Catalog, 10.0, 10.0, 12.0, 12.0).Select(item => item.Id).ToArray();

        Assert.Equal(ids, inFrame);
        Assert.Empty(none);
    }

    [Fact]
    public void A_frame_across_a_turned_item_selects_it_by_its_turned_footprint()
    {
        var scene = new Scene();
        SceneItem belt = scene.Add(Catalog.Get("belt_conveyor_4m"), 0, 0, 90);      // 4 m long, now along Z

        Assert.Single(ScenePicking.ItemsIn(scene, Catalog, -0.2, 1.5, 0.2, 1.9));
        Assert.Empty(ScenePicking.ItemsIn(scene, Catalog, 1.5, -0.2, 1.9, 0.2));
        Assert.NotNull(belt);
    }

    // ---- overlap markers

    [Fact]
    public void The_items_that_stand_through_each_other_are_the_ones_to_mark()
    {
        var scene = new Scene();
        SceneItem a = scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        SceneItem b = scene.Add(Catalog.Get("belt_conveyor_2m"), 0.5, 0);
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 5);

        string[] marked = SceneOverlap.Involved(scene, Catalog).Select(item => item.Id).ToArray();

        Assert.Equal(new[] { a.Id, b.Id }, marked);

        scene.Move(b.Id, 2.0, 0);

        Assert.Empty(SceneOverlap.Involved(scene, Catalog));
    }

    // ---- the floor

    [Fact]
    public void A_new_scene_has_the_floor_of_fourteen_by_ten_metres()
    {
        var scene = new Scene();

        Assert.Equal((14.0, 10.0), (scene.Floor.Length, scene.Floor.Width));
        Assert.True(scene.OnFloor(6.9, 4.9));
        Assert.False(scene.OnFloor(7.1, 0.0));
        Assert.False(scene.OnFloor(0.0, -5.1));
    }

    [Fact]
    public void A_floor_of_another_size_is_saved_and_opened_again_and_the_default_is_not_written()
    {
        var scene = new Scene();
        Assert.DoesNotContain("floor", scene.ToJson());

        scene.SetFloor(20.0, 12.0);
        Scene again = Scene.FromJson(scene.ToJson());

        Assert.Contains("floor", scene.ToJson());
        Assert.Equal((20.0, 12.0), (again.Floor.Length, again.Floor.Width));
        Assert.True(again.OnFloor(9.9, 5.9));

        scene.SetFloor(14.0, 10.0);
        Assert.DoesNotContain("floor", scene.ToJson());
    }

    [Fact]
    public void A_scene_file_without_a_floor_loads_with_the_default_floor()
    {
        Scene scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));

        Assert.Equal((14.0, 10.0), (scene.Floor.Length, scene.Floor.Width));
    }

    [Theory]
    [InlineData(0.0, 10.0)]
    [InlineData(14.0, -2.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(2.0, 10.0)]
    [InlineData(14.0, 500.0)]
    public void A_floor_that_is_too_small_too_large_or_not_a_number_is_refused(double length, double width)
    {
        var scene = new Scene();

        Assert.Throws<SceneException>(() => scene.SetFloor(length, width));
        Assert.Equal((14.0, 10.0), (scene.Floor.Length, scene.Floor.Width));
    }

    [Fact]
    public void The_floor_size_goes_through_undo_with_everything_else_and_is_rounded_to_half_a_metre()
    {
        var scene = new Scene();
        var history = new SceneHistory();

        history.Record(scene);
        scene.SetFloor(20.3, 12.8);
        Scene? undone = history.Undo(scene);

        Assert.Equal((20.5, 13.0), (scene.Floor.Length, scene.Floor.Width));
        Assert.Equal((14.0, 10.0), (undone!.Floor.Length, undone.Floor.Width));
        Scene? redone = history.Redo(undone);
        Assert.Equal((20.5, 13.0), (redone!.Floor.Length, redone.Floor.Width));
    }
}
