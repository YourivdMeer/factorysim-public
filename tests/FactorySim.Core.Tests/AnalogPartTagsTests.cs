using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class AnalogPartTagsTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static IReadOnlyList<TagMapEntry> TagsWith(string assetId)
    {
        var scene = new Scene();
        scene.Add(Catalog.Get(assetId), 0, 0);
        return SceneTags.Build(scene, Catalog);
    }

    [Theory]
    [InlineData("potentiometer", AssetBehaviours.Potentiometer)]
    [InlineData("digital_display", AssetBehaviours.Display)]
    [InlineData("tank", AssetBehaviours.Tank)]
    [InlineData("vision_sensor", AssetBehaviours.Vision)]
    [InlineData("alarm_siren", AssetBehaviours.Siren)]
    public void The_catalogue_gives_the_analog_parts_their_behaviour(string assetId, string behaviour)
    {
        Assert.Equal(behaviour, Catalog.Get(assetId).Behaviour);
    }

    [Fact]
    public void A_potentiometer_gives_one_int_sensor_from_0_to_1000()
    {
        TagMapEntry tag = Assert.Single(TagsWith("potentiometer"), t => t.Name == "Potentiometer");

        Assert.Equal(TagDirection.Sensor, tag.Direction);
        Assert.Equal(TagKind.Int, tag.Kind);
        Assert.Equal(ModbusArea.HoldingRegister, tag.Area);
        Assert.Equal("%IW0", tag.IecAddress);
        Assert.Equal(0, tag.Min);
        Assert.Equal(1000, tag.Max);
    }

    [Fact]
    public void A_display_gives_one_int_actuator()
    {
        TagMapEntry tag = Assert.Single(TagsWith("digital_display"), t => t.Name == "Digital display");

        Assert.Equal(TagDirection.Actuator, tag.Direction);
        Assert.Equal(ModbusArea.InputRegister, tag.Area);
        Assert.Equal("%QW0", tag.IecAddress);
        Assert.Equal("IO.oDigitalDisplay", tag.Variable);
    }

    [Fact]
    public void A_tank_gives_two_valves_a_level_and_a_flow()
    {
        IReadOnlyList<TagMapEntry> tags = TagsWith("tank");

        TagMapEntry fill = tags.Single(t => t.Name == "Tank fill valve");
        TagMapEntry discharge = tags.Single(t => t.Name == "Tank discharge valve");
        TagMapEntry scene = tags.Single(t => t.Name == "Tank level");
        TagMapEntry flow = tags.Single(t => t.Name == "Tank flow");
        Assert.Equal(new[] { 0, 1 }, new[] { fill.Offset, discharge.Offset });
        Assert.All(new[] { fill, discharge }, valve =>
        {
            Assert.Equal(ModbusArea.InputRegister, valve.Area);
            Assert.Equal("%", valve.Unit);
            Assert.Equal(100, valve.Max);
        });
        Assert.Equal(ModbusArea.HoldingRegister, scene.Area);
        Assert.Equal(100, scene.Scale);
        Assert.Equal("m", scene.Unit);
        Assert.Equal(300, scene.Max);
        Assert.Equal(10, flow.Scale);
        Assert.Equal("l/s", flow.Unit);
    }

    [Fact]
    public void A_vision_sensor_gives_a_number_and_a_bit()
    {
        IReadOnlyList<TagMapEntry> tags = TagsWith("vision_sensor");

        TagMapEntry number = tags.Single(t => t.Name == "Vision sensor");
        TagMapEntry detected = tags.Single(t => t.Name == "Vision sensor detected");
        Assert.Equal(TagKind.Int, number.Kind);
        Assert.Equal(20, number.Max);
        Assert.Null(detected.Kind);
        Assert.Equal(ModbusArea.Coil, detected.Area);
    }

    [Fact]
    public void A_siren_gives_one_bool_actuator()
    {
        TagMapEntry tag = Assert.Single(TagsWith("alarm_siren"), t => t.Name == "Siren");

        Assert.Equal(ModbusArea.DiscreteInput, tag.Area);
    }

    [Fact]
    public void The_weighing_conveyor_adds_a_weight_and_a_plain_conveyor_does_not()
    {
        TagMapEntry weight = Assert.Single(TagsWith("conveyor_scale"), t => t.Name.EndsWith(" weight", StringComparison.Ordinal));

        Assert.Equal(ModbusArea.HoldingRegister, weight.Area);
        Assert.Equal("kg", weight.Unit);
        Assert.Equal(100, weight.Scale);
        Assert.DoesNotContain(TagsWith("belt_conveyor_2m"), t => t.IsAnalog);
    }

    [Fact]
    public void A_light_array_adds_the_number_of_interrupted_beams()
    {
        var scene = new Scene();
        SceneItem receiver = scene.Add(Catalog.Get(Catalog.Assets.First(a => a.Behaviour == AssetBehaviours.LightArray).Id), 0, 0);

        TagMapEntry count = Assert.Single(SceneTags.Build(scene, Catalog), t => t.Name == receiver.Name + " count");

        Assert.Equal(TagKind.Int, count.Kind);
        Assert.Equal(8, count.Max);
    }
}
