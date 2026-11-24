using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FactorySim.Core.Plc.TestPlc;

/// <summary>
/// The built-in test PLC (plan 29): a Modbus TCP server on 127.0.0.1 with its own threads (one to accept, one per
/// client, one for the scan cycle), the C# twin of <c>tools/fake_plc/fake_plc.py</c>. Binds to loopback only:
/// Modbus TCP has no authentication (see docs/SECURITY.md). Port 0 picks a free port.
/// </summary>
public sealed class TestPlcServer : IDisposable
{
    public const string LoopbackHost = "127.0.0.1";
    public const int DefaultSize = 64;
    public const double DefaultScanMs = 10.0;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<TcpClient> _clients = new();
    private readonly List<Thread> _threads = new();
    private readonly object _gate = new();
    private readonly SorterProgram _sorter = new();
    private readonly double _scanSeconds;
    private bool _disposed;

    private TestPlcServer(TestPlcMode mode, int port, double scanMs, int size)
    {
        Mode = mode;
        Memory = new PlcMemory(size);
        _scanSeconds = scanMs / 1000.0;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public TestPlcMode Mode { get; }

    public string Host => LoopbackHost;

    public int Port { get; }

    public PlcMemory Memory { get; }

    /// <summary>Number of high boxes the sorter has pushed out.</summary>
    public int SortedCount
    {
        get
        {
            int count = 0;
            Memory.Scan((_, _, _, _) => count = _sorter.SortedCount);
            return count;
        }
    }

    /// <summary>Number of scan cycles so far.</summary>
    public long Scans => Interlocked.Read(ref _scans);

    private long _scans;

    /// <summary>Binds, then starts the scan and accept threads.</summary>
    public static TestPlcServer Start(TestPlcMode mode, int port = 0, double scanMs = DefaultScanMs, int size = DefaultSize)
    {
        var server = new TestPlcServer(mode, port, scanMs, size);
        server.StartThread(server.ScanLoop, "TestPlc scan");
        server.StartThread(server.AcceptLoop, "TestPlc accept");
        return server;
    }

    public void Dispose()
    {
        Thread[] threads;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancel.Cancel();
            _listener.Stop();
            foreach (TcpClient client in _clients)
            {
                client.Close();
            }

            threads = _threads.ToArray();
        }

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _cancel.Dispose();
    }

    private void StartThread(Action body, string name)
    {
        var thread = new Thread(() => body()) { IsBackground = true, Name = name };
        lock (_gate)
        {
            _threads.Add(thread);
        }

        thread.Start();
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint milliseconds);

    private void ScanLoop()
    {
        // Windows rounds sleeps up to 15.6 ms by default; ask for 1 ms so a 10 ms scan is 10 ms.
        bool fineTimer = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
        try
        {
            CancellationToken token = _cancel.Token;
            var clock = Stopwatch.StartNew();
            double next = 0.0;
            while (!token.IsCancellationRequested)
            {
                double now = clock.Elapsed.TotalSeconds;
                Memory.Scan((coils, holding, inputs, inputRegisters) =>
                {
                    if (Mode == TestPlcMode.Echo)
                    {
                        coils.CopyTo(inputs, 0);
                        holding.CopyTo(inputRegisters, 0);
                    }
                    else
                    {
                        _sorter.Scan(coils, now).CopyTo(inputs, 0);
                    }
                });
                Interlocked.Increment(ref _scans);

                next += _scanSeconds;
                double delay = next - clock.Elapsed.TotalSeconds;
                if (delay > 0.0)
                {
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay));
                }
                else
                {
                    next = clock.Elapsed.TotalSeconds;
                }
            }
        }
        finally
        {
            if (fineTimer)
            {
                timeEndPeriod(1);
            }
        }
    }

    private void AcceptLoop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;         // stopped
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    client.Close();
                    return;
                }

                _clients.Add(client);
            }

            StartThread(() => Serve(client), "TestPlc client");
        }
    }

    private void Serve(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            NetworkStream stream = client.GetStream();
            var header = new byte[7];
            var body = new byte[254];
            while (!_cancel.IsCancellationRequested)
            {
                if (!ReadExactly(stream, header, 7))
                {
                    return;
                }

                ushort transaction = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                byte unit = header[6];
                if (protocol != 0 || length < 2 || length > 254 || !ReadExactly(stream, body, length - 1))
                {
                    return;
                }

                byte[] response = ModbusPdu.Execute(Memory, body.AsSpan(0, length - 1));
                var frame = new byte[7 + response.Length];
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), transaction);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), (ushort)(response.Length + 1));
                frame[6] = unit;
                response.CopyTo(frame, 7);
                stream.Write(frame);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The client went away or the server stopped.
        }
        finally
        {
            client.Close();
            lock (_gate)
            {
                _clients.Remove(client);
            }
        }
    }

    private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int chunk = stream.Read(buffer, read, count - read);
            if (chunk == 0)
            {
                return false;
            }

            read += chunk;
        }

        return true;
    }
}
