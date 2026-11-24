using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FactorySim.Core.Tags;
using NModbus;

namespace FactorySim.Core.Plc;

/// <summary>
/// Modbus TCP client. Every poll it writes all bool sensor tags to the coils (FC15) and reads
/// all bool actuator tags from the discrete inputs (FC02), on its own thread. Int tags go to the
/// holding registers (FC16) and come from the input registers (FC04), only when the scene has them.
/// The PLC side is a Modbus TCP server, such as the CODESYS "Modbus TCP Slave Device".
/// </summary>
public sealed class ModbusTcpLink : IPlcLink
{
    private readonly TagStore _store;
    private readonly ModbusImage _image;
    private readonly ModbusLinkOptions _options;
    private readonly object _statsGate = new();

    private Thread? _thread;
    private CancellationTokenSource? _cancel;
    private volatile LinkStatus _status = LinkStatus.Disconnected;

    private long _exchanges;
    private long _errors;
    private long _reconnects;
    private double _lastMs;
    private double _totalMs;
    private double _maxMs;
    private string? _lastError;

    public ModbusTcpLink(TagStore store, TagMap map, ModbusLinkOptions? options = null)
    {
        _store = store;
        _image = new ModbusImage(map, store);
        _options = options ?? new ModbusLinkOptions();
    }

    public ModbusLinkOptions Options => _options;

    public LinkStatus Status => _status;

    public LinkStatistics Statistics
    {
        get
        {
            lock (_statsGate)
            {
                double average = _exchanges > 0 ? _totalMs / _exchanges : 0.0;
                return new LinkStatistics(_exchanges, _errors, _reconnects, _lastMs, average, _maxMs, _lastError);
            }
        }
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;
        _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "ModbusTcpLink" };
        _thread.Start();
    }

    public void Stop()
    {
        Thread? thread = _thread;
        if (thread is null)
        {
            return;
        }

        _cancel?.Cancel();
        thread.Join();
        _cancel?.Dispose();
        _cancel = null;
        _thread = null;
        _status = LinkStatus.Disconnected;
    }

    public void Dispose() => Stop();

    // Windows rounds waits up to its timer period, 15.6 ms by default, which would turn a
    // 10 ms poll into ~16 ms. Asking for 1 ms while the link runs keeps the poll interval honest.
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint milliseconds);

    private void Run(CancellationToken token)
    {
        bool fineTimer = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
        try
        {
            RunLoop(token);
        }
        finally
        {
            if (fineTimer)
            {
                timeEndPeriod(1);
            }

            _status = LinkStatus.Disconnected;
        }
    }

    private void RunLoop(CancellationToken token)
    {
        bool connectedBefore = false;
        while (!token.IsCancellationRequested)
        {
            _status = LinkStatus.Connecting;
            try
            {
                using TcpClient client = Connect(token);
                using IModbusMaster master = new ModbusFactory().CreateMaster(client);
                master.Transport.ReadTimeout = _options.TimeoutMs;
                master.Transport.WriteTimeout = _options.TimeoutMs;
                master.Transport.Retries = 0;

                _status = LinkStatus.Connected;
                if (connectedBefore)
                {
                    lock (_statsGate)
                    {
                        _reconnects++;
                    }
                }

                connectedBefore = true;
                ExchangeLoop(master, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e) when (e is IOException or SocketException or TimeoutException
                or InvalidOperationException or SlaveException or ObjectDisposedException)
            {
                lock (_statsGate)
                {
                    _errors++;
                    _lastError = e.Message;
                }
            }

            _status = LinkStatus.Disconnected;
            token.WaitHandle.WaitOne(_options.ReconnectDelayMs);
        }

        _status = LinkStatus.Disconnected;
    }

    private TcpClient Connect(CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_options.TimeoutMs);
            try
            {
                client.ConnectAsync(_options.Host, _options.Port, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"No answer from {_options.Host}:{_options.Port}.");
            }

            client.ReceiveTimeout = _options.TimeoutMs;
            client.SendTimeout = _options.TimeoutMs;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private void ExchangeLoop(IModbusMaster master, CancellationToken token)
    {
        var clock = new Stopwatch();
        while (!token.IsCancellationRequested)
        {
            clock.Restart();

            // All bool sensors in one request and all bool actuators in one request, so the PLC
            // never sees a mix of two simulation ticks within an area. Int tags travel in their own
            // two requests, only when the scene has them; both kinds of sensor come from the same tick.
            (bool[] bits, int[] numbers) = _store.SnapshotSensorImage();
            master.WriteMultipleCoils(_options.UnitId, 0, _image.BuildCoils(bits));
            if (_image.HoldingRegisterCount > 0)
            {
                master.WriteMultipleRegisters(_options.UnitId, 0, _image.BuildHoldingRegisters(numbers));
            }

            bool[] inputs = master.ReadInputs(_options.UnitId, 0, (ushort)_image.DiscreteInputCount);
            ushort[]? registers = _image.InputRegisterCount > 0
                ? master.ReadInputRegisters(_options.UnitId, 0, (ushort)_image.InputRegisterCount)
                : null;
            _store.ApplyActuators(_image.ExtractActuators(inputs));
            if (registers is not null)
            {
                _store.ApplyActuatorNumbers(_image.ExtractActuatorNumbers(registers));
            }

            double elapsedMs = clock.Elapsed.TotalMilliseconds;
            lock (_statsGate)
            {
                _exchanges++;
                _lastMs = elapsedMs;
                _totalMs += elapsedMs;
                _maxMs = Math.Max(_maxMs, elapsedMs);
            }

            int wait = _options.PollIntervalMs - (int)elapsedMs;
            if (wait > 0)
            {
                token.WaitHandle.WaitOne(wait);
            }
        }
    }
}
