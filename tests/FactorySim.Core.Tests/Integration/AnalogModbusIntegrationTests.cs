using System.Diagnostics;
using FactorySim.Core.Plc;
using FactorySim.Core.Tags;
using Xunit.Abstractions;

namespace FactorySim.Core.Tests.Integration;

/// <summary>
/// Int tags through ModbusTcpLink and the Python fake PLC in echo mode, which copies every holding
/// register to the input register with the same offset (and every coil to its discrete input).
/// </summary>
[Trait("Category", "Integration")]
public class AnalogModbusIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public AnalogModbusIntegrationTests(ITestOutputHelper output) => _output = output;

    /// <summary>Two bits and two numbers each way; "Knob" echoes on "Display", "Scene" on "Valve".</summary>
    private static (TagMap Map, TagStore Store) CreateScene()
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("Start", TagDirection.Sensor, ModbusArea.Coil, 0),
            new TagMapEntry("Stop", TagDirection.Sensor, ModbusArea.Coil, 1),
            new TagMapEntry("Knob", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, Kind: TagKind.Int),
            new TagMapEntry("Level", TagDirection.Sensor, ModbusArea.HoldingRegister, 1, Kind: TagKind.Int),
            new TagMapEntry("Conveyor", TagDirection.Actuator, ModbusArea.DiscreteInput, 0),
            new TagMapEntry("Lamp", TagDirection.Actuator, ModbusArea.DiscreteInput, 1),
            new TagMapEntry("Display", TagDirection.Actuator, ModbusArea.InputRegister, 0, Kind: TagKind.Int),
            new TagMapEntry("Valve", TagDirection.Actuator, ModbusArea.InputRegister, 1, Kind: TagKind.Int),
        });
        return (map, new TagStore(map.ToDefinitions()));
    }

    private static ModbusTcpLink StartLink(TagStore store, TagMap map, int port, int pollMs = 5)
    {
        var link = new ModbusTcpLink(store, map, new ModbusLinkOptions
        {
            Port = port,
            PollIntervalMs = pollMs,
            ReconnectDelayMs = 200,
        });
        link.Start();
        return link;
    }

    private static bool WaitUntil(TagStore store, Func<bool> condition, int timeoutMs = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            store.EndTick();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(2);
        }

        return false;
    }

    [Fact]
    public void Echo_numbers_come_back_on_the_input_register_with_the_same_offset()
    {
        using FakePlcProcess plc = FakePlcProcess.Start("echo");
        (TagMap map, TagStore store) = CreateScene();
        using ModbusTcpLink link = StartLink(store, map, plc.Port);
        Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected), "link did not connect");

        foreach (int number in new[] { 1234, -1, short.MinValue, short.MaxValue, 0 })
        {
            store.SetSensorNumber("Knob", number);
            store.SetSensorNumber("Level", -number / 2);
            Assert.True(WaitUntil(store, () => store.GetActuatorNumber("Display") == number
                && store.GetActuatorNumber("Valve") == -number / 2), $"echo of {number} did not arrive");
        }

        // The bits still work beside the numbers.
        store.SetSensor("Stop", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Lamp") && !store.GetActuator("Conveyor")), "bit echo did not arrive");
        Assert.Equal(0, link.Statistics.Errors);
    }

    [Fact]
    public void Numbers_flow_again_after_the_plc_restarts()
    {
        int port = FakePlcProcess.FindFreePort();
        (TagMap map, TagStore store) = CreateScene();
        FakePlcProcess plc = FakePlcProcess.Start("echo", port);
        using ModbusTcpLink link = StartLink(store, map, port);
        try
        {
            Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected), "link did not connect");
            store.SetSensorNumber("Knob", 77);
            Assert.True(WaitUntil(store, () => store.GetActuatorNumber("Display") == 77), "no echo before the restart");

            plc.Dispose();
            Assert.True(WaitUntil(store, () => link.Status != LinkStatus.Connected), "link did not notice the PLC stopping");

            plc = FakePlcProcess.Start("echo", port);
            Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected, timeoutMs: 5000),
                "link did not reconnect within 5 s");

            store.SetSensorNumber("Knob", 555);
            Assert.True(WaitUntil(store, () => store.GetActuatorNumber("Display") == 555), "no echo after the reconnect");
            Assert.True(link.Statistics.Reconnects >= 1);
        }
        finally
        {
            plc.Dispose();
        }
    }

    [Fact]
    public void Latency_with_registers()
    {
        using FakePlcProcess plc = FakePlcProcess.Start("echo", scanMs: 10.0);
        (TagMap map, TagStore store) = CreateScene();

        var roundTrips = new List<double>();
        using (ModbusTcpLink fast = StartLink(store, map, plc.Port, pollMs: 0))
        {
            Assert.True(WaitUntil(store, () => fast.Status == LinkStatus.Connected));
            long seen = 0;
            while (roundTrips.Count < 1000)
            {
                LinkStatistics stats = fast.Statistics;
                if (stats.Exchanges != seen)
                {
                    seen = stats.Exchanges;
                    roundTrips.Add(stats.LastExchangeMs);
                }
            }
        }

        double[] sorted = roundTrips.OrderBy(sample => sample).ToArray();
        double p99 = sorted[(int)Math.Ceiling(0.99 * sorted.Length) - 1];
        _output.WriteLine(
            $"Modbus round trip (FC15 + FC16 + FC02 + FC04), localhost: n={sorted.Length} min={sorted[0]:0.00} ms "
            + $"avg={roundTrips.Average():0.00} ms p50={sorted[sorted.Length / 2 - 1]:0.00} ms p99={p99:0.00} ms max={sorted[^1]:0.00} ms");
        Assert.True(p99 < 60.0, "round trip p99 above the 60 ms go/no-go limit");
    }
}
