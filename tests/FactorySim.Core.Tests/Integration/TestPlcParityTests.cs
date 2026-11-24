using System.Buffers.Binary;
using System.Net.Sockets;
using FactorySim.Core.Plc.TestPlc;
using Xunit;

namespace FactorySim.Core.Tests.Integration;

/// <summary>
/// Plan 29: the built-in test PLC answers like tools/fake_plc/fake_plc.py. The same raw Modbus frames go to both
/// servers and the answers must be the same bytes. Needs the 'py' launcher.
/// </summary>
[Trait("Category", "Integration")]
public class TestPlcParityTests
{
    private sealed class RawClient : IDisposable
    {
        private readonly TcpClient _client = new() { NoDelay = true };
        private ushort _transaction;

        public RawClient(int port) => _client.Connect("127.0.0.1", port);

        public byte[] Send(params byte[] pdu)
        {
            var frame = new byte[7 + pdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), ++_transaction);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), (ushort)(pdu.Length + 1));
            frame[6] = 1;
            pdu.CopyTo(frame, 7);
            NetworkStream stream = _client.GetStream();
            stream.Write(frame);

            var header = new byte[7];
            stream.ReadExactly(header);
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            var body = new byte[length - 1];
            stream.ReadExactly(body);
            return header.Concat(body).ToArray();
        }

        public void Dispose() => _client.Dispose();
    }

    private static readonly byte[][] Requests =
    {
        new byte[] { 15, 0, 0, 0, 10, 2, 0b0101_0101, 0b0000_0010 },
        new byte[] { 1, 0, 0, 0, 10 },
        new byte[] { 5, 0, 12, 0xFF, 0 },
        new byte[] { 1, 0, 8, 0, 8 },
        new byte[] { 5, 0, 12, 0x12, 0x34 },
        new byte[] { 16, 0, 3, 0, 3, 6, 0, 1, 0x80, 0, 0xFF, 0xFF },
        new byte[] { 6, 0, 10, 0x12, 0x34 },
        new byte[] { 3, 0, 0, 0, 12 },
        new byte[] { 1, 0, 0, 0, 0 },
        new byte[] { 2, 0, 0, 0x07, 0xD1 },
        new byte[] { 1, 0, 60, 0, 5 },
        new byte[] { 3, 0, 0, 0, 126 },
        new byte[] { 4, 0, 64, 0, 1 },
        new byte[] { 5, 0, 64, 0xFF, 0 },
        new byte[] { 6, 0, 64, 0, 1 },
        new byte[] { 15, 0, 0, 0, 9, 1, 0xFF },
        new byte[] { 16, 0, 0, 0, 124, 248 },
        new byte[] { 16, 0, 0, 0, 2, 4, 0, 1 },
        new byte[] { 3, 0, 0 },
        new byte[] { 8, 0, 0, 0, 0 },
        new byte[] { 43, 14, 1, 0 },
    };

    [Fact]
    public void Every_request_gets_the_same_answer_from_both_servers()
    {
        using FakePlcProcess python = FakePlcProcess.Start("echo");
        using var builtin = TestPlcServer.Start(TestPlcMode.Echo);
        using var toPython = new RawClient(python.Port);
        using var toBuiltin = new RawClient(builtin.Port);

        foreach (byte[] request in Requests)
        {
            Assert.Equal(Convert.ToHexString(toPython.Send(request)), Convert.ToHexString(toBuiltin.Send(request)));
        }

        Thread.Sleep(100);      // a few echo scans
        Assert.Equal(Convert.ToHexString(toPython.Send(2, 0, 0, 0, 16)), Convert.ToHexString(toBuiltin.Send(2, 0, 0, 0, 16)));
        Assert.Equal(Convert.ToHexString(toPython.Send(4, 0, 0, 0, 12)), Convert.ToHexString(toBuiltin.Send(4, 0, 0, 0, 12)));
    }

    // Coil bits of the MVP scene: 0 start, 1 stop, 2 at pusher, 3 high box, 4 pusher back, 5 pusher front, 6 sim running.
    private static readonly (string Step, byte Coils)[] SorterSteps =
    {
        ("idle", 0b101_0000),
        ("start pressed", 0b101_0001),
        ("start released", 0b101_0000),
        ("low box at pusher", 0b101_0100),
        ("high box at pusher", 0b101_1100),
        ("pusher on its way", 0b100_1100),
        ("pusher in front", 0b110_1100),
        ("pusher on its way back", 0b100_0000),
        ("pusher back", 0b101_0000),
        ("paused", 0b001_0000),
        ("running again", 0b101_0000),
        ("start again", 0b101_0001),
        ("stop pressed", 0b101_0010),
        ("stop released", 0b101_0000),
    };

    [Fact]
    public void The_sorter_gives_the_same_outputs_for_the_same_steps()
    {
        using FakePlcProcess python = FakePlcProcess.Start("sorter");
        using var builtin = TestPlcServer.Start(TestPlcMode.Sorter);
        using var toPython = new RawClient(python.Port);
        using var toBuiltin = new RawClient(builtin.Port);

        foreach ((string step, byte coils) in SorterSteps)
        {
            byte[] write = { 15, 0, 0, 0, 7, 1, coils };
            toPython.Send(write);
            toBuiltin.Send(write);
            Thread.Sleep(60);       // several scans of 10 ms
            string fromPython = Convert.ToHexString(toPython.Send(2, 0, 0, 0, 4));
            string fromBuiltin = Convert.ToHexString(toBuiltin.Send(2, 0, 0, 0, 4));
            Assert.True(fromPython == fromBuiltin, $"{step}: python {fromPython}, built-in {fromBuiltin}");
        }

        Assert.Equal(1, builtin.SortedCount);
    }
}
