using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class TagMapTests
{
    private const string Json = """
        {
          "tags": [
            { "name": "Start button", "direction": "Sensor", "area": "Coil", "offset": 0, "iecAddress": "%IX0.0", "variable": "IO.iStart", "description": "Startknop" },
            { "name": "At pusher", "direction": "Sensor", "area": "Coil", "offset": 2, "iecAddress": "%IX0.2", "variable": "IO.iAtPusher" },
            { "name": "Conveyor", "direction": "Actuator", "area": "DiscreteInput", "offset": 0, "iecAddress": "%QX0.0", "variable": "IO.oConveyor" }
          ]
        }
        """;

    [Fact]
    public void Loads_entries_from_json()
    {
        TagMap map = TagMap.FromJson(Json);

        Assert.Equal(3, map.Entries.Count);
        TagMapEntry entry = map.Entries[1];
        Assert.Equal("At pusher", entry.Name);
        Assert.Equal(TagDirection.Sensor, entry.Direction);
        Assert.Equal(ModbusArea.Coil, entry.Area);
        Assert.Equal(2, entry.Offset);
        Assert.Equal("%IX0.2", entry.IecAddress);
        Assert.Equal("IO.iAtPusher", entry.Variable);
    }

    [Fact]
    public void Json_round_trip_keeps_all_fields()
    {
        TagMap map = TagMap.FromJson(Json);

        TagMap again = TagMap.FromJson(map.ToJson());

        Assert.Equal(map.Entries, again.Entries);
    }

    [Fact]
    public void Area_sizes_cover_the_highest_offset()
    {
        TagMap map = TagMap.FromJson(Json);

        Assert.Equal(3, map.CoilCount);
        Assert.Equal(1, map.DiscreteInputCount);
    }

    [Fact]
    public void Definitions_follow_the_entry_order()
    {
        TagMap map = TagMap.FromJson(Json);

        Assert.Equal(
            new[] { "Start button", "At pusher", "Conveyor" },
            map.ToDefinitions().Select(d => d.Name));
    }

    [Fact]
    public void Csv_has_a_header_and_one_line_per_tag()
    {
        string[] lines = TagMap.FromJson(Json).ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.Equal("Name;Direction;Area;Offset;IecAddress;Variable;Description;Kind;Scale;Unit;Min;Max", lines[0].TrimEnd('\r'));
        Assert.Equal("Start button;Sensor;Coil;0;%IX0.0;IO.iStart;Startknop;Bool;;;;", lines[1].TrimEnd('\r'));
    }

    [Fact]
    public void Csv_quotes_fields_that_contain_the_separator()
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0, Description: "een; twee \"drie\""),
        });

        Assert.Contains("\"een; twee \"\"drie\"\"\"", map.ToCsv());
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        var entries = new[]
        {
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0),
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 1),
        };

        Assert.Throws<TagMapException>(() => new TagMap(entries));
    }

    [Fact]
    public void Duplicate_addresses_are_rejected()
    {
        var entries = new[]
        {
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 4),
            new TagMapEntry("B", TagDirection.Sensor, ModbusArea.Coil, 4),
        };

        Assert.Throws<TagMapException>(() => new TagMap(entries));
    }

    [Theory]
    [InlineData(TagDirection.Sensor, ModbusArea.DiscreteInput)]
    [InlineData(TagDirection.Actuator, ModbusArea.Coil)]
    public void Area_must_match_the_direction(TagDirection direction, ModbusArea area)
    {
        var entries = new[] { new TagMapEntry("A", direction, area, 0) };

        Assert.Throws<TagMapException>(() => new TagMap(entries));
    }

    [Fact]
    public void Negative_offsets_and_broken_json_are_rejected()
    {
        Assert.Throws<TagMapException>(() => new TagMap(new[]
        {
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, -1),
        }));
        Assert.Throws<TagMapException>(() => TagMap.FromJson("{ not json"));
        Assert.Throws<TagMapException>(() => TagMap.FromJson("{}"));
    }

    [Fact]
    public void Default_tag_map_of_the_mvp_scene_is_valid()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "tagmap.json");

        TagMap map = TagMap.FromJson(File.ReadAllText(path));

        Assert.Equal(11, map.Entries.Count);
        Assert.Equal(7, map.CoilCount);
        Assert.Equal(4, map.DiscreteInputCount);
        Assert.Contains(map.Entries, e => e.Name == "Sim running" && e.Variable == "IO.iSimRunning");
    }
}
