using FactorySim.Core.Plc;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class AnalogModbusImageTests
{
    private static (TagMap Map, TagStore Store) Create()
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("Start", TagDirection.Sensor, ModbusArea.Coil, 0),
            new TagMapEntry("Level", TagDirection.Sensor, ModbusArea.HoldingRegister, 2, Kind: TagKind.Int),
            new TagMapEntry("Knob", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, Kind: TagKind.Int),
            new TagMapEntry("Lamp", TagDirection.Actuator, ModbusArea.DiscreteInput, 0),
            new TagMapEntry("Display", TagDirection.Actuator, ModbusArea.InputRegister, 1, Kind: TagKind.Int),
            new TagMapEntry("Valve", TagDirection.Actuator, ModbusArea.InputRegister, 0, Kind: TagKind.Int, Min: 0, Max: 100),
        });
        return (map, new TagStore(map.ToDefinitions()));
    }

    [Fact]
    public void Register_counts_match_the_tag_map()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        Assert.Equal(1, image.CoilCount);
        Assert.Equal(1, image.DiscreteInputCount);
        Assert.Equal(3, image.HoldingRegisterCount);
        Assert.Equal(2, image.InputRegisterCount);
    }

    [Fact]
    public void Analog_sensors_land_on_their_register_offsets_as_16_bit_words()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);
        store.SetSensorNumber("Level", -1);
        store.SetSensorNumber("Knob", short.MaxValue);
        store.EndTick();

        ushort[] registers = image.BuildHoldingRegisters(store.SnapshotSensorNumbers());

        Assert.Equal(new ushort[] { 0x7FFF, 0, 0xFFFF }, registers);
    }

    [Fact]
    public void Input_registers_are_read_back_with_their_sign()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        int[] numbers = image.ExtractActuatorNumbers(new ushort[] { 40, 0x8000 });

        // AnalogActuatorNames is [Display, Valve]: Display <- register 1, Valve <- register 0.
        Assert.Equal(new[] { "Display", "Valve" }, store.AnalogActuatorNames);
        Assert.Equal(new[] { (int)short.MinValue, 40 }, numbers);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(short.MinValue)]
    [InlineData(short.MaxValue)]
    [InlineData(0)]
    [InlineData(1234)]
    public void A_number_survives_the_trip_through_a_register(int number)
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("In", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, Kind: TagKind.Int),
            new TagMapEntry("Out", TagDirection.Actuator, ModbusArea.InputRegister, 0, Kind: TagKind.Int),
        });
        var store = new TagStore(map.ToDefinitions());
        var image = new ModbusImage(map, store);
        store.SetSensorNumber("In", number);
        store.EndTick();

        ushort[] registers = image.BuildHoldingRegisters(store.SnapshotSensorNumbers());
        store.ApplyActuatorNumbers(image.ExtractActuatorNumbers(registers));
        store.EndTick();

        Assert.Equal(number, store.GetActuatorNumber("Out"));
    }

    [Fact]
    public void Too_few_input_registers_is_an_error()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        Assert.Throws<ArgumentException>(() => image.ExtractActuatorNumbers(new ushort[] { 1 }));
    }

    [Fact]
    public void A_digital_scene_has_no_registers()
    {
        var map = new TagMap(new[] { new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0) });
        var image = new ModbusImage(map, new TagStore(map.ToDefinitions()));

        Assert.Equal(0, image.HoldingRegisterCount);
        Assert.Equal(0, image.InputRegisterCount);
        Assert.Empty(image.BuildHoldingRegisters(Array.Empty<int>()));
    }
}
