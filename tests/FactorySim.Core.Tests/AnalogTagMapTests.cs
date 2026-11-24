using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class AnalogTagMapTests
{
    private const string Json = """
        {
          "tags": [
            { "name": "Start", "direction": "Sensor", "area": "Coil", "offset": 0 },
            { "name": "Level", "direction": "Sensor", "area": "HoldingRegister", "offset": 1, "iecAddress": "%IW1", "kind": "Int", "scale": 100, "unit": "m", "min": 0, "max": 300 },
            { "name": "Valve", "direction": "Actuator", "area": "InputRegister", "offset": 0, "iecAddress": "%QW0", "kind": "Int", "unit": "%" }
          ]
        }
        """;

    [Fact]
    public void Loads_analog_entries_and_counts_registers()
    {
        TagMap map = TagMap.FromJson(Json);

        TagMapEntry scene = map.Entries[1];
        Assert.Equal(TagKind.Int, scene.Kind);
        Assert.Equal(100, scene.Scale);
        Assert.Equal("m", scene.Unit);
        Assert.Equal(0, scene.Min);
        Assert.Equal(300, scene.Max);
        Assert.Equal(1, map.CoilCount);
        Assert.Equal(0, map.DiscreteInputCount);
        Assert.Equal(2, map.HoldingRegisterCount);
        Assert.Equal(1, map.InputRegisterCount);
    }

    [Fact]
    public void An_old_entry_without_a_kind_is_digital()
    {
        TagMap map = TagMap.FromJson(Json);

        Assert.Null(map.Entries[0].Kind);
        Assert.False(map.Entries[0].IsAnalog);
        Assert.Equal(TagKind.Bool, map.ToDefinitions().First().Kind);
    }

    [Fact]
    public void Digital_entries_are_written_without_analog_fields()
    {
        var map = new TagMap(new[] { new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0) });

        string json = map.ToJson();

        Assert.DoesNotContain("kind", json);
        Assert.DoesNotContain("scale", json);
    }

    [Fact]
    public void Json_round_trip_keeps_the_analog_fields()
    {
        TagMap map = TagMap.FromJson(Json);

        TagMap again = TagMap.FromJson(map.ToJson());

        Assert.Equal(map.Entries, again.Entries);
    }

    [Fact]
    public void Definitions_carry_kind_scale_and_range()
    {
        TagDefinition scene = TagMap.FromJson(Json).ToDefinitions().Single(d => d.Name == "Level");

        Assert.Equal(new TagDefinition("Level", TagDirection.Sensor, TagKind.Int, 100, "m", 0, 300), scene);
    }

    [Theory]
    [InlineData(TagDirection.Sensor, ModbusArea.Coil)]
    [InlineData(TagDirection.Actuator, ModbusArea.DiscreteInput)]
    [InlineData(TagDirection.Actuator, ModbusArea.HoldingRegister)]
    public void An_analog_tag_in_the_wrong_area_is_refused(TagDirection direction, ModbusArea area)
    {
        var entries = new[] { new TagMapEntry("A", direction, area, 0, Kind: TagKind.Int) };

        Assert.Throws<TagMapException>(() => new TagMap(entries));
    }

    [Fact]
    public void A_digital_tag_in_a_register_is_refused()
    {
        var entries = new[] { new TagMapEntry("A", TagDirection.Sensor, ModbusArea.HoldingRegister, 0) };

        Assert.Throws<TagMapException>(() => new TagMap(entries));
    }

    [Fact]
    public void Coil_and_holding_register_may_share_an_offset()
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0),
            new TagMapEntry("B", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, Kind: TagKind.Int),
        });

        Assert.Equal(1, map.CoilCount);
        Assert.Equal(1, map.HoldingRegisterCount);
    }

    [Fact]
    public void Csv_has_columns_for_kind_scale_and_unit()
    {
        string[] lines = TagMap.FromJson(Json).ToCsv().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Name;Direction;Area;Offset;IecAddress;Variable;Description;Kind;Scale;Unit;Min;Max", lines[0]);
        Assert.Equal("Start;Sensor;Coil;0;;;;Bool;;;;", lines[1]);
        Assert.Equal("Level;Sensor;HoldingRegister;1;%IW1;;;Int;100;m;0;300", lines[2]);
    }

    [Fact]
    public void Analog_tags_get_register_addresses_apart_from_the_bits()
    {
        var wanted = new[]
        {
            new TagDefinition("Start", TagDirection.Sensor),
            new TagDefinition("Knob", TagDirection.Sensor, TagKind.Int, Min: 0, Max: 1000),
            new TagDefinition("Lamp", TagDirection.Actuator),
            new TagDefinition("Display", TagDirection.Actuator, TagKind.Int),
        };

        IReadOnlyList<TagMapEntry> tags = SceneTags.Assign(wanted, Array.Empty<TagMapEntry>());

        Assert.Equal(new[] { "Start", "Knob", "Lamp", "Display" }, tags.Select(t => t.Name));
        TagMapEntry knob = tags[1];
        Assert.Equal(ModbusArea.HoldingRegister, knob.Area);
        Assert.Equal(0, knob.Offset);
        Assert.Equal("%IW0", knob.IecAddress);
        Assert.Equal("IO.iKnob", knob.Variable);
        Assert.Equal(TagKind.Int, knob.Kind);
        Assert.Equal(1000, knob.Max);
        TagMapEntry display = tags[3];
        Assert.Equal(ModbusArea.InputRegister, display.Area);
        Assert.Equal("%QW0", display.IecAddress);
        Assert.Null(display.Min);
        Assert.Null(display.Max);
        _ = new TagMap(tags);
    }

    [Fact]
    public void Saved_register_addresses_stay_and_the_range_follows_the_part()
    {
        var saved = new[]
        {
            new TagMapEntry("Knob", TagDirection.Sensor, ModbusArea.HoldingRegister, 3, "%IW3", "IO.iKnob", Kind: TagKind.Int, Max: 10),
        };
        var wanted = new[]
        {
            new TagDefinition("Knob", TagDirection.Sensor, TagKind.Int, Min: 0, Max: 1000),
            new TagDefinition("Other", TagDirection.Sensor, TagKind.Int),
        };

        IReadOnlyList<TagMapEntry> tags = SceneTags.Assign(wanted, saved);

        TagMapEntry knob = tags.Single(t => t.Name == "Knob");
        Assert.Equal(3, knob.Offset);
        Assert.Equal(1000, knob.Max);
        Assert.Equal(0, tags.Single(t => t.Name == "Other").Offset);
    }

    [Fact]
    public void A_saved_digital_scene_is_written_without_analog_fields()
    {
        Scene scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));
        scene.Tags.Clear();
        scene.Tags.AddRange(SceneTags.Build(scene, AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")))));

        string json = scene.ToJson();

        Assert.DoesNotContain("\"kind\"", json);
        Assert.DoesNotContain("Register", json);
    }

    [Fact]
    public void A_tag_that_turned_analog_gets_a_new_address()
    {
        var saved = new[] { new TagMapEntry("Knob", TagDirection.Sensor, ModbusArea.Coil, 5, "%IX0.5") };
        var wanted = new[] { new TagDefinition("Knob", TagDirection.Sensor, TagKind.Int) };

        TagMapEntry knob = Assert.Single(SceneTags.Assign(wanted, saved));

        Assert.Equal(ModbusArea.HoldingRegister, knob.Area);
        Assert.Equal(0, knob.Offset);
    }
}
