using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 02, part C: chutes, free rollers, aligner and corner stop; plan 13: the decor pieces.
/// Colliders come from the catalogue.</summary>
public class PassivePartTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    public static TheoryData<string> PassiveIds => new()
    {
        "free_roller", "chute", "chute_low", "aligner", "metal_corner",
        "electric_switchboard", "column", "handrail_2m", "platform_2x2", "stairs", "safeguard_2m",
    };

    [Theory]
    [MemberData(nameof(PassiveIds))]
    public void A_passive_part_has_colliders_but_no_tags_and_no_settings(string id)
    {
        CatalogAsset asset = Catalog.Get(id);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Passive, asset.Behaviour);
        Assert.True(asset.IsFunctional && asset.IsPlaceable);          // not greyed out in the library
        Assert.NotEmpty(asset.Colliders!);
        Assert.Empty(SceneTags.TagsOf(item, asset));
        Assert.Empty(ItemParams.Editable(asset));
        Assert.Empty(SceneSupports.For(item, asset));
    }

    [Theory]
    [MemberData(nameof(PassiveIds))]
    public void Every_collider_is_a_sound_box_that_lies_around_the_model(string id)
    {
        CatalogAsset asset = Catalog.Get(id);

        foreach (ColliderSpec collider in asset.Colliders!)
        {
            Assert.Equal(3, collider.Size.Length);
            Assert.Equal(3, collider.Position.Length);
            Assert.All(collider.Size, side => Assert.InRange(side, 0.005, 6.0));
            Assert.InRange(collider.Friction, 0.0, 1.0);
            Assert.True(collider.Yaw == 0.0 || collider.Pitch == 0.0, "a collider is turned about one axis only");

            // The centre must lie inside the model's bounding box (x, y up, z); a mix-up of the
            // Blender and Godot axes puts it far outside.
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.InRange(collider.Position[axis], asset.BoundsMin[axis] - 0.05, asset.BoundsMax[axis] + 0.05);
            }
        }
    }

    [Fact]
    public void A_chute_slopes_down_along_its_length_and_is_slippery_enough_to_slide_a_box()
    {
        foreach ((string id, double drop) in new[] { ("chute", 0.5), ("chute_low", 0.3) })
        {
            CatalogAsset asset = Catalog.Get(id);
            double expected = Math.Atan2(drop, 1.5) * 180.0 / Math.PI;
            ColliderSpec[] slopes = asset.Colliders!.Where(collider => collider.Pitch != 0.0).ToArray();

            Assert.NotEmpty(slopes);
            Assert.All(slopes, slope => Assert.Equal(expected, slope.Pitch, 2));
            // A box slides when the friction is below the tangent of the slope; the box has 0.7 and the
            // two are combined as their square root at worst.
            double slide = Math.Tan(expected * Math.PI / 180.0);
            Assert.All(slopes, slope => Assert.True(Math.Sqrt(slope.Friction * 0.7) < slide * 0.8,
                $"{id}: friction {slope.Friction} too high for a slope of {expected:F1} degrees"));
            // A side wall on each side: tall and thin.
            Assert.Equal(2, asset.Colliders!.Count(collider => collider.Pitch != 0.0 && collider.Size[1] > 0.05 && collider.Size[2] < 0.1));
        }
    }

    [Fact]
    public void The_aligner_has_two_mirrored_guides_that_close_to_a_gap_a_small_box_just_fits_through()
    {
        ColliderSpec[] guides = Catalog.Get("aligner").Colliders!;

        Assert.Equal(2, guides.Length);
        Assert.Equal(guides[0].Position[2], -guides[1].Position[2], 6);       // mirrored across the centre line
        Assert.Equal(guides[0].Yaw, -guides[1].Yaw, 6);
        Assert.NotEqual(0.0, guides[0].Yaw);
        Assert.Equal(guides[0].Position[0], guides[1].Position[0], 6);

        // Gap at the narrow end: the inner faces of the two guides at the downstream end.
        double gap = 0.0;
        foreach (ColliderSpec guide in guides)
        {
            double half = guide.Size[0] / 2.0;
            double yaw = guide.Yaw * Math.PI / 180.0;
            double endX = guide.Position[0] + half * Math.Cos(yaw);
            double endZ = guide.Position[2] - half * Math.Sin(yaw);            // positive yaw turns +X towards -Z
            double inner = Math.Abs(endZ) - guide.Size[2] / 2.0 / Math.Cos(yaw);
            gap += inner;
            Assert.True(endX > 0.0);
        }

        Assert.InRange(gap, Box30 + 0.02, Box30 + 0.08);
    }

    private const double Box30 = 0.30;      // the standard box is 0.30 m wide

    [Fact]
    public void The_free_roller_is_a_flat_slip_surface_at_roller_height_and_the_corner_stop_has_two_plates()
    {
        ColliderSpec roller = Assert.Single(Catalog.Get("free_roller").Colliders!);
        Assert.Equal(0.0, roller.Pitch);
        Assert.Equal(0.0, roller.Yaw);
        Assert.True(roller.Friction < 0.05);
        Assert.Equal(0.0, roller.Position[1] + roller.Size[1] / 2.0, 3);       // the top is at the item height
        Assert.Equal(1.0, roller.Size[0], 3);                                  // as long as the model

        ColliderSpec[] plates = Catalog.Get("metal_corner").Colliders!;
        Assert.Equal(2, plates.Length);
        Assert.Contains(plates, plate => plate.Size[0] > plate.Size[2]);        // one along the travel
        Assert.Contains(plates, plate => plate.Size[2] > plate.Size[0]);        // one across it
    }

    [Theory]
    [InlineData("electric_switchboard")]
    [InlineData("column")]
    [InlineData("safeguard_2m")]
    public void A_cabinet_a_column_and_a_safeguard_are_one_box_over_the_whole_model(string id)
    {
        CatalogAsset asset = Catalog.Get(id);
        ColliderSpec box = Assert.Single(asset.Colliders!);

        Assert.Equal(0.0, box.Yaw);
        Assert.Equal(0.0, box.Pitch);
        Assert.Equal(0.0, box.Position[1] - box.Size[1] / 2.0, 2);                  // from the floor
        Assert.InRange(box.Position[1] + box.Size[1] / 2.0, asset.BoundsMax[1] - 0.1, asset.BoundsMax[1] + 0.01);
        Assert.InRange(box.Size[0], asset.Size[0] - 0.15, asset.Size[0] + 0.01);    // over the whole width
    }

    [Fact]
    public void The_platform_deck_lies_on_the_deck_height_and_stands_on_four_legs()
    {
        CatalogAsset asset = Catalog.Get("platform_2x2");
        double deck = asset.Param("deck_height", 0.0);
        ColliderSpec plate = asset.Colliders!.OrderByDescending(c => c.Position[1] + c.Size[1] / 2.0).First();

        Assert.Equal(1.0, deck);
        Assert.InRange(plate.Position[1] + plate.Size[1] / 2.0, deck - 0.005, deck + 0.02);
        Assert.True(plate.Size[0] > 1.9 && plate.Size[2] > 1.9, "the deck covers the platform");
        ColliderSpec[] legs = asset.Colliders!.Where(c => c.Size[1] > 0.5).ToArray();
        Assert.Equal(4, legs.Length);
        Assert.All(legs, leg => Assert.Equal(0.0, leg.Position[1] - leg.Size[1] / 2.0, 2));
    }

    [Fact]
    public void The_stairs_have_one_plate_rising_along_x_over_the_treads_and_two_stringers()
    {
        CatalogAsset asset = Catalog.Get("stairs");
        ColliderSpec[] sloped = asset.Colliders!.Where(c => c.Pitch != 0.0).ToArray();

        // Positive pitch lowers the +X end; the treads rise towards +X, so the pitch is negative.
        Assert.All(sloped, c => Assert.True(c.Pitch < -20.0 && c.Pitch > -60.0, $"pitch {c.Pitch}"));
        ColliderSpec plate = Assert.Single(sloped, c => c.Size[2] > 0.5);
        Assert.Equal(2, sloped.Count(c => c.Size[2] < 0.1));
        Assert.Equal(0.0, plate.Yaw);
    }

    [Fact]
    public void The_handrail_has_rails_and_posts_and_leaves_a_gap_under_the_middle_rail()
    {
        CatalogAsset asset = Catalog.Get("handrail_2m");
        ColliderSpec[] rails = asset.Colliders!.Where(c => c.Size[0] > 1.5).ToArray();
        ColliderSpec[] posts = asset.Colliders!.Where(c => c.Size[1] > 0.5).ToArray();

        Assert.True(rails.Count(c => c.Position[1] > 0.3) >= 2, "a top rail and a middle rail");
        Assert.True(posts.Length >= 2);
        Assert.All(asset.Colliders!, c => Assert.True(c.Size[2] <= 0.06, "thin across"));
        ColliderSpec middle = rails.Where(c => c.Position[1] > 0.3).OrderBy(c => c.Position[1]).First();
        double lowerRailTop = rails.Where(c => c.Position[1] < middle.Position[1]).Select(c => c.Position[1] + c.Size[1] / 2.0).DefaultIfEmpty(0.0).Max();
        Assert.True(middle.Position[1] - middle.Size[1] / 2.0 - lowerRailTop > 0.2, "a small item fits under the middle rail");
    }
}
