using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class AnalogIoListTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static IReadOnlyList<IoPoint> Points()
    {
        var entries = new[]
        {
            new TagMapEntry("Start", TagDirection.Sensor, ModbusArea.Coil, 0, "%IX0.0", "IO.iStart"),
            new TagMapEntry("Level", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, "%IW0", "IO.iLevel",
                Kind: TagKind.Int, Scale: 100, Unit: "m"),
            new TagMapEntry("Stop", TagDirection.Sensor, ModbusArea.Coil, 1, "%IX0.1", "IO.iStop"),
            new TagMapEntry("Lamp", TagDirection.Actuator, ModbusArea.DiscreteInput, 0, "%QX0.0", "IO.oLamp"),
            new TagMapEntry("Valve", TagDirection.Actuator, ModbusArea.InputRegister, 0, "%QW0", "IO.oValve", Kind: TagKind.Int),
        };
        return IoList.Build(new Scene(), Catalog, entries);
    }

    [Theory]
    [InlineData(ModbusArea.Coil, "Coil 3")]
    [InlineData(ModbusArea.DiscreteInput, "Discrete input 3")]
    [InlineData(ModbusArea.HoldingRegister, "Holding register 3")]
    [InlineData(ModbusArea.InputRegister, "Input register 3")]
    public void Modbus_text_names_the_area(ModbusArea area, string expected)
    {
        var tag = new TagMapEntry("A", TagDirection.Sensor, area, 3);

        Assert.Equal(expected, IoList.ModbusText(tag));
    }

    [Fact]
    public void Inputs_list_the_bits_before_the_words()
    {
        Assert.Equal(new[] { "Start", "Stop", "Level" }, IoList.PlcInputs(Points()).Select(p => p.Tag.Name));
        Assert.Equal(new[] { "Lamp", "Valve" }, IoList.PlcOutputs(Points()).Select(p => p.Tag.Name));
    }

    [Fact]
    public void The_analog_filter_keeps_only_int_tags()
    {
        string[] analog = Points().Where(p => IoList.Matches(p, TagFilter.Analog, null, forced: false))
            .Select(p => p.Tag.Name).ToArray();

        Assert.Equal(new[] { "Level", "Valve" }, analog);
    }

    [Fact]
    public void Search_finds_a_register_address()
    {
        Assert.Single(Points(), p => IoList.Matches(p, TagFilter.All, "%IW", forced: false));
        Assert.Single(Points(), p => IoList.Matches(p, TagFilter.All, "holding", forced: false));
    }

    [Fact]
    public void Io_csv_says_the_type_of_each_tag()
    {
        string[] lines = IoList.ToCsv(Points()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Kind;IecAddress;Modbus;Variable;Part;Signal;Asset;Type;Unit", lines[0]);
        Assert.StartsWith("Input;%IX0.0;Coil 0;IO.iStart;", lines[1]);
        Assert.EndsWith(";BOOL;", lines[1]);
        Assert.StartsWith("Input;%IW0;Holding register 0;IO.iLevel;", lines[3]);
        Assert.EndsWith(";INT;m", lines[3]);
    }

    [Fact]
    public void Gvl_declares_int_tags_as_INT_in_their_own_group()
    {
        string gvl = GvlExport.ToSt(Points(), "Test");

        Assert.Contains("iLevel : INT;", gvl.Replace("  ", string.Empty));
        Assert.Contains("oValve : INT;", gvl.Replace("  ", string.Empty));
        Assert.Contains("iStart : BOOL;", gvl.Replace("  ", string.Empty));
        Assert.Contains("(* App -> PLC: Holding Registers *)", gvl);
        Assert.Contains("(* PLC -> App: Input Registers *)", gvl);
        Assert.Contains("Holding Register 0, %IW0", gvl);
        Assert.True(gvl.IndexOf("iStop", StringComparison.Ordinal) < gvl.IndexOf("iLevel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(123, 100, "m", "1,23 m  (123)")]
    [InlineData(-5, 100, null, "-0,05  (-5)")]
    [InlineData(40, 1, "%", "40 %")]
    [InlineData(7, 1, null, "7")]
    [InlineData(25, 10, "°C", "2,5 °C  (25)")]
    public void An_analog_value_is_shown_with_its_unit_and_the_number_on_the_line(int number, double scale, string? unit, string expected)
    {
        Assert.Equal(expected, AnalogText.Format(number, scale, unit));
    }
}
