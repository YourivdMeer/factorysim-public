using System.Diagnostics;
using FactorySim.Core.Plc;
using FactorySim.Core.Plc.TestPlc;
using FactorySim.Core.Tags;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>
/// Plan 29: the built-in test PLC. The sorter and the Modbus handling follow tools/fake_plc/fake_plc.py step by step;
/// <c>TestPlcParityTests</c> (Integration) compares both over the wire.
/// </summary>
public class TestPlcTests
{
    // Coil offsets of the MVP scene, as in fake_plc.py.
    private const int Start = 0, Stop = 1, AtPusher = 2, HighBox = 3, PusherBack = 4, PusherFront = 5, SimRunning = 6;
    private const int Conveyor = 0, Pusher = 1, Emitter = 2, RunLamp = 3;

    private static bool[] Coils(params int[] high)
    {
        var coils = new bool[64];
        foreach (int index in high)
        {
            coils[index] = true;
        }

        return coils;
    }

    // ------------------------------------------------------------------ sorter

    [Fact]
    public void Start_latches_run_and_stop_or_a_stopped_simulation_drops_it()
    {
        var sorter = new SorterProgram();

        bool[] idle = sorter.Scan(Coils(SimRunning, PusherBack), 0.0);
        Assert.False(idle[Conveyor] || idle[Emitter] || idle[RunLamp]);

        bool[] started = sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.01);
        Assert.True(started[Conveyor] && started[Emitter] && started[RunLamp]);
        Assert.False(started[Pusher]);

        Assert.True(sorter.Scan(Coils(SimRunning, PusherBack), 0.02)[RunLamp]);     // latched
        Assert.False(sorter.Scan(Coils(SimRunning, PusherBack, Stop), 0.03)[RunLamp]);
        Assert.True(sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.04)[RunLamp]);
        Assert.False(sorter.Scan(Coils(PusherBack), 0.05)[RunLamp]);                // simulation stopped
    }

    [Fact]
    public void A_high_box_at_the_pusher_is_pushed_out_and_the_pusher_returns()
    {
        var sorter = new SorterProgram();
        sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.0);

        bool[] push = sorter.Scan(Coils(SimRunning, PusherBack, AtPusher, HighBox), 0.1);
        Assert.Equal(10, sorter.Step);
        Assert.True(push[Pusher]);
        Assert.False(push[Conveyor]);       // the belt waits while pushing

        bool[] back = sorter.Scan(Coils(SimRunning, PusherFront), 0.5);
        Assert.Equal(20, sorter.Step);
        Assert.False(back[Pusher]);
        Assert.Equal(1, sorter.SortedCount);

        bool[] done = sorter.Scan(Coils(SimRunning, PusherBack), 0.9);
        Assert.Equal(0, sorter.Step);
        Assert.True(done[Conveyor]);
    }

    [Fact]
    public void A_low_box_is_not_pushed()
    {
        var sorter = new SorterProgram();
        sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.0);

        bool[] outputs = sorter.Scan(Coils(SimRunning, PusherBack, AtPusher), 0.1);

        Assert.Equal(0, sorter.Step);
        Assert.False(outputs[Pusher]);
        Assert.True(outputs[Conveyor]);
    }

    [Fact]
    public void The_watchdog_trips_after_three_seconds_and_start_clears_the_fault()
    {
        var sorter = new SorterProgram();
        sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.0);
        sorter.Scan(Coils(SimRunning, PusherBack, AtPusher, HighBox), 1.0);      // step 10
        sorter.Scan(Coils(SimRunning, AtPusher, HighBox), 1.01);                  // the watchdog starts here

        sorter.Scan(Coils(SimRunning, AtPusher, HighBox), 3.9);
        Assert.False(sorter.Fault);
        bool[] tripped = sorter.Scan(Coils(SimRunning, AtPusher, HighBox), 4.1);
        Assert.True(sorter.Fault);
        Assert.Equal(0, sorter.Step);
        Assert.False(tripped[Pusher]);
        // As in fake_plc.py, run is evaluated before the watchdog: the lamp drops on the next scan.
        Assert.False(sorter.Scan(Coils(SimRunning, AtPusher, HighBox), 4.15)[RunLamp]);

        // Fault is cleared on the scan with Start, run follows on the next one (same order as fake_plc.py).
        sorter.Scan(Coils(SimRunning, PusherBack, Start), 4.2);
        Assert.False(sorter.Fault);
        Assert.True(sorter.Scan(Coils(SimRunning, PusherBack, Start), 4.3)[RunLamp]);
    }

    [Fact]
    public void The_watchdog_does_not_run_while_the_simulation_is_paused()
    {
        var sorter = new SorterProgram();
        sorter.Scan(Coils(SimRunning, PusherBack, Start), 0.0);
        sorter.Scan(Coils(SimRunning, PusherBack, AtPusher, HighBox), 1.0);
        sorter.Scan(Coils(SimRunning, AtPusher, HighBox), 1.01);

        sorter.Scan(Coils(AtPusher, HighBox), 10.0);         // paused for a long time: no fault
        Assert.False(sorter.Fault);
        Assert.Equal(10, sorter.Step);
    }

    // ------------------------------------------------------------------ Modbus PDUs

    private static PlcMemory Memory() => new(64);

    [Fact]
    public void Write_and_read_coils_round_trip()
    {
        PlcMemory memory = Memory();

        // FC15: 10 coils from 3, bits 1 0 1 1 0 0 0 0 | 1 1
        byte[] written = ModbusPdu.Execute(memory, new byte[] { 15, 0, 3, 0, 10, 2, 0b0000_1101, 0b0000_0011 });
        Assert.Equal(new byte[] { 15, 0, 3, 0, 10 }, written);

        byte[] read = ModbusPdu.Execute(memory, new byte[] { 1, 0, 3, 0, 10 });
        Assert.Equal(new byte[] { 1, 2, 0b0000_1101, 0b0000_0011 }, read);

        // FC05: single coil, only 0x0000 and 0xFF00 are valid.
        Assert.Equal(new byte[] { 5, 0, 0, 0xFF, 0 }, ModbusPdu.Execute(memory, new byte[] { 5, 0, 0, 0xFF, 0 }));
        Assert.True(memory.Coil(0));
        Assert.Equal(new byte[] { 0x85, 3 }, ModbusPdu.Execute(memory, new byte[] { 5, 0, 0, 0x12, 0x34 }));
    }

    [Fact]
    public void Discrete_inputs_and_input_registers_are_read_from_the_outputs_of_the_program()
    {
        PlcMemory memory = Memory();
        memory.SetOutputs(new[] { true, false, true, true }, new ushort[] { 7, 65535 });

        Assert.Equal(new byte[] { 2, 1, 0b0000_1101 }, ModbusPdu.Execute(memory, new byte[] { 2, 0, 0, 0, 4 }));
        Assert.Equal(new byte[] { 4, 4, 0, 7, 0xFF, 0xFF }, ModbusPdu.Execute(memory, new byte[] { 4, 0, 0, 0, 2 }));
    }

    [Fact]
    public void Holding_registers_are_written_with_fc6_and_fc16_and_read_with_fc3()
    {
        PlcMemory memory = Memory();

        Assert.Equal(new byte[] { 6, 0, 1, 0x01, 0x02 }, ModbusPdu.Execute(memory, new byte[] { 6, 0, 1, 0x01, 0x02 }));
        Assert.Equal(new byte[] { 16, 0, 2, 0, 2 }, ModbusPdu.Execute(memory, new byte[] { 16, 0, 2, 0, 2, 4, 0, 9, 0x80, 0 }));

        Assert.Equal(new byte[] { 3, 6, 0x01, 0x02, 0, 9, 0x80, 0 }, ModbusPdu.Execute(memory, new byte[] { 3, 0, 1, 0, 3 }));
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0, 0, 0 }, 0x81, 3)]               // count 0
    [InlineData(new byte[] { 2, 0, 0, 0x07, 0xD1 }, 0x82, 3)]         // count 2001
    [InlineData(new byte[] { 1, 0, 60, 0, 5 }, 0x81, 2)]              // past the end of 64
    [InlineData(new byte[] { 3, 0, 0, 0, 126 }, 0x83, 3)]             // more than 125 registers
    [InlineData(new byte[] { 4, 0, 64, 0, 1 }, 0x84, 2)]
    [InlineData(new byte[] { 5, 0, 64, 0xFF, 0 }, 0x85, 2)]
    [InlineData(new byte[] { 6, 0, 64, 0, 1 }, 0x86, 2)]
    [InlineData(new byte[] { 15, 0, 0, 0, 9, 1, 0xFF }, 0x8F, 3)]     // byte count does not match 9 coils
    [InlineData(new byte[] { 15, 0, 0, 0x07, 0xB1, 0xF7 }, 0x8F, 3)]  // 1969 coils
    [InlineData(new byte[] { 16, 0, 0, 0, 124, 248 }, 0x90, 3)]       // more than 123 registers
    [InlineData(new byte[] { 16, 0, 0, 0, 2, 4, 0, 1 }, 0x90, 3)]     // data shorter than the byte count
    [InlineData(new byte[] { 3, 0, 0 }, 0x83, 3)]                      // truncated
    [InlineData(new byte[] { 8, 0, 0, 0, 0 }, 0x88, 1)]               // diagnostics: not supported
    public void Bad_requests_get_the_same_exception_codes_as_the_python_fake_plc(byte[] pdu, byte function, byte code)
    {
        Assert.Equal(new[] { function, code }, ModbusPdu.Execute(Memory(), pdu));
    }

    // ------------------------------------------------------------------ the server with the real client

    private static (TagMap Map, TagStore Store) MvpTags()
    {
        TagMap map = TagMap.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tagmap.json")));
        return (map, new TagStore(map.ToDefinitions()));
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
    public void The_server_listens_on_loopback_on_a_free_port_and_stops_cleanly()
    {
        using var server = TestPlcServer.Start(TestPlcMode.Sorter);

        Assert.True(server.Port > 0);
        Assert.NotEqual(502, server.Port);
        Assert.Equal("127.0.0.1", server.Host);
        server.Dispose();
        Assert.Throws<System.Net.Sockets.SocketException>(() =>
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("127.0.0.1", server.Port);
        });
    }

    [Fact]
    public void The_modbus_link_of_the_app_drives_the_sorter_in_the_server()
    {
        (TagMap map, TagStore store) = MvpTags();
        using var server = TestPlcServer.Start(TestPlcMode.Sorter);
        using var link = new ModbusTcpLink(store, map, new ModbusLinkOptions { Port = server.Port, PollIntervalMs = 5, ReconnectDelayMs = 200 });
        link.Start();

        store.SetSensor("Sim running", true);
        store.SetSensor("Pusher back", true);
        store.SetSensor("Start button", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Conveyor") && store.GetActuator("Emitter")), "Start did not start the line");
        store.SetSensor("Start button", false);

        store.SetSensor("At pusher", true);
        store.SetSensor("High box", true);
        Assert.True(WaitUntil(store, () => store.GetActuator("Pusher") && !store.GetActuator("Conveyor")), "a high box was not pushed");

        store.SetSensor("Stop button", true);
        Assert.True(WaitUntil(store, () => !store.GetActuator("Run lamp")), "Stop did not stop the line");
        Assert.Equal(LinkStatus.Connected, link.Status);
        Assert.Equal(0, link.Statistics.Errors);
    }

    [Fact]
    public void In_echo_mode_every_coil_and_holding_register_comes_back()
    {
        using var server = TestPlcServer.Start(TestPlcMode.Echo);
        server.Memory.WriteCoils(5, new[] { true });
        server.Memory.WriteHolding(2, new ushort[] { 1234 });

        Assert.True(SpinWait.SpinUntil(() =>
            ModbusPdu.Execute(server.Memory, new byte[] { 2, 0, 5, 0, 1 })[2] == 1
            && ModbusPdu.Execute(server.Memory, new byte[] { 4, 0, 2, 0, 1 }) is [4, 2, 0x04, 0xD2], 1000));
    }
}
