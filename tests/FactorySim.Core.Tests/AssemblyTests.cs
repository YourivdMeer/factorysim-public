using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 04, part B: a lid set on a base makes a final product.</summary>
public class AssemblyTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    [Theory]
    [InlineData("product_base_blue", "product_lid_blue", "final_product_blue")]
    [InlineData("product_base_green", "product_lid_green", "final_product_green")]
    [InlineData("product_base_metal", "product_lid_metal", "final_product_metal")]
    public void A_lid_on_a_base_of_the_same_colour_makes_the_final_product_of_that_colour(string baseId, string lidId, string expected)
    {
        Assert.Equal(expected, Assembly.Result(baseId, lidId));
    }

    [Theory]
    [InlineData("product_base_blue", "product_lid_green", "final_product_blue")]
    [InlineData("product_base_metal", "product_lid_blue", "final_product_metal")]
    [InlineData("product_base_green", "product_lid_metal", "final_product_green")]
    public void With_mixed_colours_the_final_product_has_the_colour_of_the_base(string baseId, string lidId, string expected)
    {
        Assert.Equal(expected, Assembly.Result(baseId, lidId));
    }

    [Theory]
    [InlineData("product_lid_blue", "product_lid_blue")]            // a lid is no base
    [InlineData("product_base_blue", "product_base_blue")]          // a base is no lid
    [InlineData("product_lid_blue", "product_base_blue")]           // the wrong way round
    [InlineData("raw_material_blue", "product_lid_blue")]
    [InlineData("product_base_blue", "raw_material_blue")]
    [InlineData("final_product_blue", "product_lid_blue")]
    [InlineData("pallet", "product_lid_blue")]
    [InlineData("product_base_", "product_lid_blue")]               // no colour
    [InlineData(null, "product_lid_blue")]
    [InlineData("product_base_blue", null)]
    [InlineData(null, null)]
    public void Anything_else_does_not_join(string? baseId, string? lidId)
    {
        Assert.Null(Assembly.Result(baseId, lidId));
    }

    [Fact]
    public void Every_result_is_an_item_of_the_library()
    {
        foreach (string colour in new[] { "blue", "green", "metal" })
        {
            string? result = Assembly.Result("product_base_" + colour, "product_lid_" + colour);

            Assert.NotNull(result);
            Assert.Equal(AssetBehaviours.Item, Catalog.Get(result!).Behaviour);
        }
    }

    [Theory]
    [InlineData(0.0, 0.0, true)]
    [InlineData(0.02, 0.0, true)]
    [InlineData(0.0, -0.02, true)]
    [InlineData(0.02, 0.02, true)]          // 2.8 cm diagonally
    [InlineData(0.03, 0.0, false)]          // exactly 3 cm is too far
    [InlineData(0.025, 0.025, false)]       // 3.5 cm diagonally
    [InlineData(0.2, 0.0, false)]
    public void A_lid_must_lie_within_three_centimetres_of_the_middle_of_the_base(double dx, double dz, bool expected)
    {
        Assert.Equal(expected, Assembly.Aligned(1.0, 2.0, 1.0 + dx, 2.0 + dz));
    }

    [Fact]
    public void The_final_product_is_as_tall_as_a_base_and_a_lid_together_give_or_less_and_stands_on_the_same_floor()
    {
        CatalogAsset baseAsset = Catalog.Get("product_base_blue");
        CatalogAsset lid = Catalog.Get("product_lid_blue");
        CatalogAsset final = Catalog.Get("final_product_blue");

        Assert.InRange(final.Size[1], baseAsset.Size[1], baseAsset.Size[1] + lid.Size[1] + 0.05);
        Assert.Equal(baseAsset.Size[0], final.Size[0], 3);              // the same footprint
        Assert.Equal(baseAsset.Size[2], final.Size[2], 3);
    }

    [Fact]
    public void The_example_scene_opens_without_problems_and_has_the_station_the_stop_the_bars_and_two_chosen_items()
    {
        Scene scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assembly_line.fsscene.json")));

        Assert.Equal("Assemblage", scene.Name);
        Assert.Empty(SceneTags.Problems(scene, Catalog));
        Assert.Empty(SceneOverlap.Find(scene, Catalog));
        Assert.Empty(SensorPairing.Unpaired(scene, Catalog));
        foreach (string asset in new[] { "pick_and_place", "stop_blade", "positioning_bars", "remover_hood_1m0" })
        {
            Assert.Contains(scene.Items, item => item.Asset == asset);
        }

        Assert.Equal(new[] { "product_lid_blue", "product_base_blue" }, scene.Items.Where(item => item.Emits is not null).Select(item => item.Emits));
        // The tags a PLC program for it needs.
        IReadOnlyList<string> names = SceneTags.Build(scene, Catalog).Select(entry => entry.Name).ToArray();
        foreach (string tag in new[] { "Pick and place X", "Pick and place Y", "Pick and place Z", "Pick and place grab", "Pick and place item",
                     "Stop", "Clamp", "Clamp open", "Clamp closed", "Sensor", "Sensor 2", "Conveyor", "Conveyor 2", "Emitter", "Emitter 2" })
        {
            Assert.Contains(tag, names);
        }
    }
}
