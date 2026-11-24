using System.Diagnostics;
using FactorySim.Core.Plc;
using FactorySim.Core.Tags;
using Xunit.Abstractions;

namespace FactorySim.Core.Tests.Integration;

/// <summary>
/// ModbusTcpLink against the Python fake PLC. Needs the 'py' launcher (Python 3.9+).
/// The tests play the simulation: they set sensor tags and end ticks, like the scene does.
/// </summary>
[Trait("Category", "Integration")]
public class ModbusLinkIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ModbusLinkIntegrationTests(ITestOutputHelper output) => _output = output;

    private static (TagMap Map, TagStore Store) LoadScene()
    {
        TagMap map = TagMap.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tagmap.json")));
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

    /// <summary>Keeps ending simulation ticks until the condition holds.</summary>
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
    public void Echo_sensor_comes_back_on_the_actuator_with_the_same_offset()
    {
        using FakePlcProcess plc = FakePlcProcess.Start("echo");
        (TagMap map, TagStore store) = LoadScene();
        using ModbusTcpLink link = StartLink(store, map, plc.Port);

        Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected), "link did not connect");

        // "At pusher" is coil 2; in echo mode that is discrete input 2, the "Emitter" tag.
        store.SetSensor("At pusher", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Emitter")), "echo did not arrive");
        Assert.False(store.GetActuator("Conveyor"));
        Assert.False(store.GetActuator("Pusher"));

        store.SetSensor("At pusher", false);
        Assert.True(WaitUntil(store, () => !store.GetActuator("Emitter")), "echo did not clear");
        Assert.Equal(0, link.Statistics.Errors);
    }

    [Fact]
    public void Sorter_runs_the_start_stop_and_push_sequence()
    {
        using FakePlcProcess plc = FakePlcProcess.Start("sorter");
        (TagMap map, TagStore store) = LoadScene();
        using ModbusTcpLink link = StartLink(store, map, plc.Port);
        Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected), "link did not connect");

        // Idle: simulation running, pusher at the back, nothing started.
        store.SetSensor("Sim running", true);
        store.SetSensor("Pusher back", true);
        store.EndTick();
        Thread.Sleep(100);
        store.EndTick();
        Assert.False(store.GetActuator("Conveyor"));

        // Start (pulse): belt, emitter and lamp on.
        store.SetSensor("Start button", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Conveyor")), "belt did not start");
        store.SetSensor("Start button", false);
        Assert.True(WaitUntil(store, () => store.GetActuator("Emitter") && store.GetActuator("Run lamp")));
        Assert.False(store.GetActuator("Pusher"));

        // A low box passes: nothing happens.
        store.SetSensor("At pusher", true);
        Thread.Sleep(100);
        store.EndTick();
        Assert.True(store.GetActuator("Conveyor"));
        Assert.False(store.GetActuator("Pusher"));
        store.SetSensor("At pusher", false);

        // A high box arrives: belt stops, pusher extends.
        store.SetSensor("At pusher", true);
        store.SetSensor("High box", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Pusher") && !store.GetActuator("Conveyor")),
            "pusher did not extend for a high box");

        // Pusher reaches the front: retract, belt still stopped.
        store.SetSensor("Pusher back", false);
        store.SetSensor("Pusher front", true);
        store.SetSensor("At pusher", false);
        store.SetSensor("High box", false);
        Assert.True(WaitUntil(store, () => !store.GetActuator("Pusher")), "pusher did not retract");
        Assert.False(store.GetActuator("Conveyor"));

        // Pusher back home: belt runs again.
        store.SetSensor("Pusher front", false);
        store.SetSensor("Pusher back", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Conveyor")), "belt did not resume");

        // Stop: everything off.
        store.SetSensor("Stop button", true);
        Assert.True(WaitUntil(store, () => !store.GetActuator("Conveyor") && !store.GetActuator("Emitter")
            && !store.GetActuator("Run lamp")), "stop did not switch everything off");
    }

    [Fact]
    public void Link_reconnects_after_the_plc_restarts()
    {
        int port = FakePlcProcess.FindFreePort();
        (TagMap map, TagStore store) = LoadScene();
        FakePlcProcess plc = FakePlcProcess.Start("echo", port);
        using ModbusTcpLink link = StartLink(store, map, port);
        try
        {
            Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected), "link did not connect");

            plc.Dispose();
            Assert.True(WaitUntil(store, () => link.Status != LinkStatus.Connected), "link did not notice the PLC stopping");

            plc = FakePlcProcess.Start("echo", port);
            Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected, timeoutMs: 5000),
                "link did not reconnect within 5 s");

            store.SetSensor("Start button", true);
            Assert.True(WaitUntil(store, () => store.GetActuator("Conveyor")), "no exchange after reconnect");
            Assert.True(link.Statistics.Reconnects >= 1);
            Assert.True(link.Statistics.Errors >= 1);
        }
        finally
        {
            plc.Dispose();
        }
    }

    [Fact]
    public void Link_reports_disconnected_when_nothing_listens()
    {
        (TagMap map, TagStore store) = LoadScene();
        using ModbusTcpLink link = StartLink(store, map, FakePlcProcess.FindFreePort());

        Thread.Sleep(600);

        Assert.NotEqual(LinkStatus.Connected, link.Status);
        Assert.True(link.Statistics.Errors >= 1);
        Assert.NotNull(link.Statistics.LastError);
    }

    [Fact]
    public void Latency_round_trip_and_sensor_to_actuator()
    {
        using FakePlcProcess plc = FakePlcProcess.Start("echo", scanMs: 10.0);
        (TagMap map, TagStore store) = LoadScene();

        // 1. Raw Modbus round trip (write coils + read inputs), polling as fast as possible.
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

        Report("Modbus round trip (FC15 + FC02), localhost", roundTrips);

        // 2. Sensor edge to actuator echo with the settings the app uses: poll 10 ms, PLC scan 10 ms.
        var edges = new List<double>();
        using (ModbusTcpLink link = StartLink(store, map, plc.Port, pollMs: 10))
        {
            Assert.True(WaitUntil(store, () => link.Status == LinkStatus.Connected));
            bool value = false;
            for (int i = 0; i < 200; i++)
            {
                value = !value;
                bool target = value;
                // Vary the phase so the edge does not always fall at the same point of the poll cycle.
                Thread.Sleep(i % 7);
                store.SetSensor("Start button", target);
                var clock = Stopwatch.StartNew();
                while (true)
                {
                    store.EndTick();
                    if (store.GetActuator("Conveyor") == target)
                    {
                        break;
                    }

                    Assert.True(clock.ElapsedMilliseconds < 2000, "echo timed out");
                    Thread.SpinWait(200);
                }

                edges.Add(clock.Elapsed.TotalMilliseconds);
            }
        }

        Report("Sensor edge -> actuator (poll 10 ms, scan 10 ms)", edges);

        Assert.True(Percentile(roundTrips, 0.99) < 60.0, "round trip p99 above the 60 ms go/no-go limit");
    }

    private void Report(string title, List<double> samples)
    {
        _output.WriteLine(
            $"{title}: n={samples.Count} min={samples.Min():0.00} ms avg={samples.Average():0.00} ms "
            + $"p50={Percentile(samples, 0.50):0.00} ms p99={Percentile(samples, 0.99):0.00} ms max={samples.Max():0.00} ms");
    }

    private static double Percentile(List<double> samples, double fraction)
    {
        double[] sorted = samples.OrderBy(sample => sample).ToArray();
        int index = Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
