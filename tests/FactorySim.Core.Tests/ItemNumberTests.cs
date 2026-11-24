namespace FactorySim.Core.Tests;

public class ItemNumberTests
{
    [Theory]
    [InlineData("box_low", 1)]
    [InlineData("pallet", 6)]
    [InlineData("raw_material_blue", 9)]
    [InlineData("final_product_metal", 20)]
    [InlineData("unknown_thing", 0)]
    [InlineData(null, 0)]
    public void A_vision_sensor_numbers_each_kind_of_item(string? itemId, int expected)
    {
        Assert.Equal(expected, ItemKinds.Number(itemId));
    }

    [Fact]
    public void Every_item_in_the_library_has_its_own_number()
    {
        var catalog = Scenes.AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));
        string[] items = catalog.Assets.Where(asset => asset.Behaviour == Scenes.AssetBehaviours.Item).Select(asset => asset.Id).ToArray();

        Assert.All(items, id => Assert.NotEqual(0, ItemKinds.Number(id)));
        Assert.Equal(items.Length, items.Select(ItemKinds.Number).Distinct().Count());
    }
}
