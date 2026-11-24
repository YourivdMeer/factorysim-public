using FactorySim.Core.Plc;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class ModbusImageTests
{
    private static (TagMap Map, TagStore Store) Create()
    {
        // Deliberately out of order and with a gap, to prove offsets come from the map, not from list order.
        var map = new TagMap(new[]
        {
            new TagMapEntry("B", TagDirection.Sensor, ModbusArea.Coil, 3),
            new TagMapEntry("Out2", TagDirection.Actuator, ModbusArea.DiscreteInput, 2),
            new TagMapEntry("A", TagDirection.Sensor, ModbusArea.Coil, 0),
            new TagMapEntry("Out0", TagDirection.Actuator, ModbusArea.DiscreteInput, 0),
        });
        return (map, new TagStore(map.ToDefinitions()));
    }

    [Fact]
    public void Sensors_land_on_their_coil_offsets()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);
        store.SetSensor("B", true);
        store.EndTick();

        bool[] coils = image.BuildCoils(store.SnapshotSensors());

        Assert.Equal(new[] { false, false, false, true }, coils);
    }

    [Fact]
    public void Discrete_inputs_are_read_back_in_actuator_order()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        bool[] actuators = image.ExtractActuators(new[] { false, true, true });

        // ActuatorNames is [Out2, Out0]: Out2 <- input 2 (true), Out0 <- input 0 (false).
        Assert.Equal(new[] { "Out2", "Out0" }, store.ActuatorNames);
        Assert.Equal(new[] { true, false }, actuators);
    }

    [Fact]
    public void Counts_match_the_tag_map()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        Assert.Equal(4, image.CoilCount);
        Assert.Equal(3, image.DiscreteInputCount);
    }

    [Fact]
    public void Too_few_discrete_inputs_is_an_error()
    {
        (TagMap map, TagStore store) = Create();
        var image = new ModbusImage(map, store);

        Assert.Throws<ArgumentException>(() => image.ExtractActuators(new[] { true }));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.5.6.7", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("plc.example.com", false)]
    [InlineData("", false)]
    public void Loopback_hosts_are_recognised(string host, bool expected)
    {
        Assert.Equal(expected, ModbusLinkOptions.IsLoopback(host));
    }

    [Fact]
    public void Default_options_point_at_localhost()
    {
        var options = new ModbusLinkOptions();

        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(502, options.Port);
        Assert.True(ModbusLinkOptions.IsLoopback(options.Host));
    }
}
