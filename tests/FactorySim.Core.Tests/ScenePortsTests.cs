using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 17: the ends of belts and curves, and dragging one so that it clicks onto another.</summary>
public class ScenePortsTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private const double Tolerance = 1e-6;

    private static SceneItem Add(Scene scene, string asset, double x, double z, double yaw = 0.0) =>
        scene.Add(Catalog.Get(asset), x, z, yaw);

    private static void AssertAt(PortSnap? snap, double x, double z, double yaw)
    {
        Assert.NotNull(snap);
        Assert.Equal(x, snap!.X, 6);
        Assert.Equal(z, snap.Z, 6);
        Assert.Equal(yaw, snap.Yaw, 6);
    }

    // ---- the ports themselves

    [Fact]
    public void A_straight_belt_has_an_in_at_the_back_and_an_out_at_the_front()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "belt_conveyor_4m", 1.0, 2.0);

        var ports = ScenePorts.Ports(belt, Catalog.Get(belt.Asset));

        Assert.Equal(2, ports.Count);
        ScenePort input = ports.Single(p => p.IsIn);
        ScenePort output = ports.Single(p => !p.IsIn);
        Assert.Equal((-1.0, 2.0, 0.8), (input.X, input.Z, input.Y));
        Assert.Equal((3.0, 2.0, 0.8), (output.X, output.Z, output.Y));
        Assert.Equal((1.0, 0.0), (input.DirX, input.DirZ));
        Assert.Equal((1.0, 0.0), (output.DirX, output.DirZ));
    }

    [Fact]
    public void A_curve_takes_in_at_radius_in_x_and_puts_out_at_radius_in_minus_z()
    {
        var scene = new Scene();
        SceneItem curve = Add(scene, "belt_conveyor_curved", 0.0, 0.0);

        var ports = ScenePorts.Ports(curve, Catalog.Get(curve.Asset));

        ScenePort input = ports.Single(p => p.IsIn);
        ScenePort output = ports.Single(p => !p.IsIn);
        Assert.Equal(1.0, input.X, 6);
        Assert.Equal(0.0, input.Z, 6);
        Assert.Equal(0.0, input.DirX, 6);
        Assert.Equal(-1.0, input.DirZ, 6);
        Assert.Equal(0.0, output.X, 6);
        Assert.Equal(-1.0, output.Z, 6);
        Assert.Equal(-1.0, output.DirX, 6);
        Assert.Equal(0.0, output.DirZ, 6);
    }

    [Fact]
    public void An_inclined_belt_is_low_at_the_in_and_high_at_the_out_and_reverse_swaps_them()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "belt_conveyor_inclined", 0.0, 0.0);

        var ports = ScenePorts.Ports(belt, Catalog.Get(belt.Asset));
        Assert.Equal(0.8, ports.Single(p => p.IsIn).Y, 6);
        Assert.Equal(1.8, ports.Single(p => !p.IsIn).Y, 6);
        Assert.Equal(-2.0, ports.Single(p => p.IsIn).X, 6);

        scene.SetParam(belt.Id, SceneTags.Reverse, 1.0);
        var reversed = ScenePorts.Ports(scene.Find(belt.Id)!, Catalog.Get(belt.Asset));
        ScenePort input = reversed.Single(p => p.IsIn);
        Assert.Equal((2.0, 1.8), (input.X, input.Y));
        Assert.Equal(-1.0, input.DirX, 6);
        Assert.Equal(0.8, reversed.Single(p => !p.IsIn).Y, 6);
    }

    [Fact]
    public void A_two_way_belt_has_both_an_in_and_an_out_at_each_end()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        scene.SetParam(belt.Id, SceneTags.TwoWay, 1.0);

        var ports = ScenePorts.Ports(scene.Find(belt.Id)!, Catalog.Get(belt.Asset));

        Assert.Equal(4, ports.Count);
        Assert.Equal(2, ports.Count(p => p.IsIn));
        Assert.Contains(ports, p => p.IsIn && p.X == -1.0 && p.DirX == 1.0);
        Assert.Contains(ports, p => !p.IsIn && p.X == -1.0 && p.DirX == -1.0);
    }

    [Fact]
    public void Other_parts_have_no_ports()
    {
        var scene = new Scene();
        SceneItem sensor = Add(scene, "diffuse_sensor", 0.0, 0.0);
        Assert.Empty(ScenePorts.Ports(sensor, Catalog.Get(sensor.Asset)));
    }

    // ---- snapping

    [Fact]
    public void A_curve_clicks_onto_the_end_of_a_belt_as_in_the_scenes_example()
    {
        // SCENES.md: a belt along +X ending at x = 1, z = 0, and a curve with radius 1.0 m → centre (1, -1), yaw 270.
        var scene = new Scene();
        Add(scene, "belt_conveyor_4m", -1.0, 0.0);
        SceneItem curve = Add(scene, "belt_conveyor_curved", 5.0, 5.0, 270.0);

        PortSnap? snap = ScenePorts.Snap(scene, Catalog, new[] { curve.Id }, curve.Id, 1.1, -0.9);

        AssertAt(snap, 1.0, -1.0, 270.0);
        Assert.Equal(0.0, snap!.TurnDegrees, 6);
        Assert.Equal((1.0, 0.0), (Math.Round(snap.PortX, 6), Math.Round(snap.PortZ, 6)));
    }

    [Fact]
    public void A_curve_that_points_the_wrong_way_is_turned_in_quarter_turns()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_4m", -1.0, 0.0);
        SceneItem curve = Add(scene, "belt_conveyor_curved", 5.0, 5.0);       // yaw 0

        PortSnap? snap = ScenePorts.Snap(scene, Catalog, new[] { curve.Id }, curve.Id, 1.1, -0.9);

        AssertAt(snap, 1.0, -1.0, 270.0);
        Assert.Equal(270.0, snap!.TurnDegrees, 6);
    }

    [Fact]
    public void A_belt_after_the_curve_clicks_onto_its_out()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_4m", -1.0, 0.0);
        SceneItem curve = Add(scene, "belt_conveyor_curved", 0.0, 0.0, 270.0);
        scene.Place(curve.Id, 1.0, -1.0, 270.0);
        SceneItem belt = Add(scene, "belt_conveyor_2m", 5.0, 5.0, 90.0);        // runs along -Z

        // The curve leaves at (2, -1) towards -Z; a 2 m belt along -Z starts there with its centre at (2, -2).
        PortSnap? snap = ScenePorts.Snap(scene, Catalog, new[] { belt.Id }, belt.Id, 2.1, -2.15);

        AssertAt(snap, 2.0, -2.0, 90.0);
    }

    [Theory]
    [InlineData(0.2, true)]
    [InlineData(0.25, true)]
    [InlineData(0.3, false)]
    public void Belt_onto_belt_in_line_clicks_within_a_quarter_metre(double gap, bool clicks)
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0);

        PortSnap? snap = ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0 + gap, 0.0);

        if (clicks)
        {
            AssertAt(snap, 2.0, 0.0, 0.0);
        }
        else
        {
            Assert.Null(snap);
        }
    }

    [Fact]
    public void Two_belts_whose_flows_meet_head_on_never_click()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0, 180.0);    // its out faces the out of the first

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0, 0.0));
        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.005, 0.0));
        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, -2.0, 0.0));
    }

    [Fact]
    public void A_two_way_belt_clicks_at_both_ends()
    {
        var scene = new Scene();
        SceneItem twoWay = Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem after = Add(scene, "belt_conveyor_2m", 5.0, 5.0);
        SceneItem before = Add(scene, "belt_conveyor_2m", 5.0, 9.0, 180.0);    // runs along -X, into the back of the first

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { before.Id }, before.Id, -2.1, 0.0));

        scene.SetParam(twoWay.Id, SceneTags.TwoWay, 1.0);
        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { after.Id }, after.Id, 2.1, 0.0), 2.0, 0.0, 0.0);
        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { before.Id }, before.Id, -2.1, 0.0), -2.0, 0.0, 180.0);
    }

    [Fact]
    public void A_belt_two_centimetres_higher_does_not_click()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0);
        int index = scene.Items.FindIndex(item => item.Id == moving.Id);
        scene.Items[index] = moving with { Y = moving.Y + 0.02 };

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.1, 0.0));

        scene.Items[index] = moving with { Y = moving.Y + 0.005 };              // within a centimetre
        Assert.NotNull(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.1, 0.0));
    }

    [Fact]
    public void A_belt_does_not_click_onto_a_roller_conveyor_on_the_floor()
    {
        var scene = new Scene();
        Add(scene, "roller_conveyor_2m", 0.0, 0.0);                             // surface at 0.5 m, the belt at 0.8 m
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0);

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.1, 0.0));
    }

    [Fact]
    public void A_port_that_is_already_taken_does_not_click()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        Add(scene, "belt_conveyor_2m", 2.0, 0.0);                               // already on the out at (1, 0)
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0);

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.1, 0.0));
        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 4.1, 0.0), 4.0, 0.0, 0.0);   // the free end
    }

    [Fact]
    public void The_group_being_dragged_is_not_something_to_click_onto()
    {
        var scene = new Scene();
        SceneItem a = Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem b = Add(scene, "belt_conveyor_2m", 2.0, 0.0);

        // Dragging both: nothing else is left to click onto.
        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { a.Id, b.Id }, a.Id, 0.1, 0.0));
        // Alone, a clicks back onto b: b's in is free while a is being dragged.
        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { a.Id }, a.Id, 0.1, 0.0), 0.0, 0.0, 0.0);
    }

    [Fact]
    public void Of_two_candidates_the_nearest_wins()
    {
        var scene = new Scene();
        SceneItem upper = Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        scene.Place(upper.Id, 0.0, 0.15, 0.0);                                  // out at (1, 0.15)
        Add(scene, "belt_conveyor_2m", 0.0, -1.0);
        SceneItem lower = scene.Items[^1];
        scene.Place(lower.Id, 0.0, -0.05, 0.0);                                 // out at (1, -0.05)
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0);

        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0, 0.0), 2.0, -0.05, 0.0);
        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0, 0.1), 2.0, 0.15, 0.0);
    }

    [Theory]
    [InlineData(90.0, 0.1, -2.1, 0.0, -2.0)]
    [InlineData(180.0, -2.1, 0.1, -2.0, 0.0)]
    [InlineData(270.0, -0.1, 2.1, 0.0, 2.0)]
    public void Turned_belts_click_in_line(double yaw, double x, double z, double expectedX, double expectedZ)
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0, yaw);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0, yaw);

        AssertAt(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, x, z), expectedX, expectedZ, yaw);
    }

    [Fact]
    public void A_straight_belt_is_never_turned_to_fit()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0, 90.0);      // across the first

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0, 0.0));
    }

    [Fact]
    public void A_belt_not_on_a_quarter_turn_never_clicks()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem moving = Add(scene, "belt_conveyor_2m", 5.0, 5.0, 45.0);

        Assert.Null(ScenePorts.Snap(scene, Catalog, new[] { moving.Id }, moving.Id, 2.0, 0.0));
    }

    [Fact]
    public void A_new_belt_from_the_library_clicks_too()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);

        AssertAt(ScenePorts.SnapNew(scene, Catalog, Catalog.Get("spur_conveyor"), 0.0, 1.9, 0.1), 1.8, 0.0, 0.0);
        Assert.Null(ScenePorts.SnapNew(scene, Catalog, Catalog.Get("roller_conveyor_2m"), 0.0, 2.0, 0.0));
    }

    [Fact]
    public void A_group_of_three_moves_and_turns_as_one()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_4m", -1.0, 0.0);                              // out at (1, 0)
        SceneItem curve = Add(scene, "belt_conveyor_curved", 5.0, 5.0);         // yaw 0: has to turn 270
        SceneItem belt = Add(scene, "belt_conveyor_2m", 5.0, 3.0, 90.0);
        SceneItem sensor = Add(scene, "diffuse_sensor", 6.0, 5.0);
        string[] group = { curve.Id, belt.Id, sensor.Id };
        double[] before = group.Select(id => Distance(scene.Find(id)!, scene.Find(curve.Id)!)).ToArray();

        PortSnap? snap = ScenePorts.Snap(scene, Catalog, group, curve.Id, 1.1, -0.9);
        AssertAt(snap, 1.0, -1.0, 270.0);
        scene.TransformMany(group, curve.X, curve.Z, snap!.TurnDegrees, snap.X - curve.X, snap.Z - curve.Z);

        SceneItem movedCurve = scene.Find(curve.Id)!;
        Assert.Equal((1.0, -1.0, 270.0), (movedCurve.X, movedCurve.Z, movedCurve.Yaw));
        double[] after = group.Select(id => Distance(scene.Find(id)!, movedCurve)).ToArray();
        Assert.Equal(before, after, (a, b) => Math.Abs(a - b) < 0.002);
        Assert.Equal(0.0, scene.Find(belt.Id)!.Yaw, 6);                         // 90 + 270
        Assert.Equal(270.0, scene.Find(sensor.Id)!.Yaw, 6);
        // Turned 270 (+X towards +Z) about the curve: the belt 2 m in -Z of the curve is now 2 m in +X of it.
        Assert.Equal(3.0, scene.Find(belt.Id)!.X, 3);
        Assert.Equal(-1.0, scene.Find(belt.Id)!.Z, 3);
    }

    [Fact]
    public void A_group_moved_without_a_turn_shifts_all_by_the_same_amount()
    {
        var scene = new Scene();
        Add(scene, "belt_conveyor_2m", 0.0, 0.0);
        SceneItem a = Add(scene, "belt_conveyor_2m", 5.0, 5.0);
        SceneItem b = Add(scene, "belt_conveyor_2m", 7.0, 5.0);
        SceneItem c = Add(scene, "diffuse_sensor", 6.0, 5.75);
        string[] group = { a.Id, b.Id, c.Id };

        PortSnap? snap = ScenePorts.Snap(scene, Catalog, group, a.Id, 2.1, 0.05);
        AssertAt(snap, 2.0, 0.0, 0.0);
        scene.TransformMany(group, a.X, a.Z, snap!.TurnDegrees, snap.X - a.X, snap.Z - a.Z);

        Assert.Equal((2.0, 0.0), (scene.Find(a.Id)!.X, scene.Find(a.Id)!.Z));
        Assert.Equal((4.0, 0.0), (scene.Find(b.Id)!.X, scene.Find(b.Id)!.Z));
        Assert.Equal((3.0, 0.75), (scene.Find(c.Id)!.X, scene.Find(c.Id)!.Z));
    }

    [Fact]
    public void Moving_a_clicked_together_group_keeps_the_ends_together()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "belt_conveyor_4m", 0.0, 0.0);              // out at (2, 0)
        SceneItem spur = Add(scene, "spur_conveyor", 5.0, 5.0);
        scene.Place(spur.Id, 2.8, 0.0, 0.0);                                     // 1.6 m: centre off the grid

        scene.MoveMany(new[] { belt.Id, spur.Id }, 1.0, 0.25);

        Assert.Equal((1.0, 0.25), (scene.Find(belt.Id)!.X, scene.Find(belt.Id)!.Z));
        Assert.Equal((3.8, 0.25), (scene.Find(spur.Id)!.X, scene.Find(spur.Id)!.Z));
    }

    private static (Scene Scene, string[] Ids) ClickedPair()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "belt_conveyor_4m", 0.0, 0.0);              // out at (2, 0)
        SceneItem spur = Add(scene, "spur_conveyor", 5.0, 5.0);
        scene.Place(spur.Id, 2.8, 0.0, 0.0);                                     // 1.6 m: centre off the grid
        return (scene, new[] { belt.Id, spur.Id });
    }

    private static bool EndsMeet(Scene scene, string fromId, string toId)
    {
        SceneItem from = scene.Find(fromId)!;
        SceneItem to = scene.Find(toId)!;
        var outs = ScenePorts.Ports(from, Catalog.Get(from.Asset)).Where(p => !p.IsIn);
        var ins = ScenePorts.Ports(to, Catalog.Get(to.Asset)).Where(p => p.IsIn).ToArray();
        return outs.Any(o => ins.Any(i => Math.Abs(o.X - i.X) < 0.0015 && Math.Abs(o.Z - i.Z) < 0.0015));
    }

    [Fact]
    public void Turning_a_clicked_together_group_keeps_the_ends_together()
    {
        (Scene scene, string[] ids) = ClickedPair();
        (double cx, double cz) = scene.Centre(ids);

        scene.RotateMany(ids, 90.0, cx, cz);

        Assert.True(EndsMeet(scene, ids[0], ids[1]));
    }

    [Fact]
    public void Duplicating_or_pasting_a_clicked_together_group_keeps_the_ends_together()
    {
        (Scene scene, string[] ids) = ClickedPair();

        var copies = scene.DuplicateMany(ids, 0.5, 2.0);
        Assert.True(EndsMeet(scene, copies[0].Id, copies[1].Id));

        var pasted = scene.PasteMany(ids.Select(id => scene.Find(id)!).ToArray(), -3.1, -2.6);
        Assert.True(EndsMeet(scene, pasted[0].Id, pasted[1].Id));
    }

    [Fact]
    public void Place_keeps_positions_off_the_grid_to_the_millimetre()
    {
        var scene = new Scene();
        SceneItem belt = Add(scene, "spur_conveyor", 0.0, 0.0);

        scene.Place(belt.Id, 1.80004, -0.1234, 450.0);

        Assert.Equal((1.8, -0.123, 90.0), (scene.Find(belt.Id)!.X, scene.Find(belt.Id)!.Z, scene.Find(belt.Id)!.Yaw));
    }

    private static double Distance(SceneItem a, SceneItem b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
