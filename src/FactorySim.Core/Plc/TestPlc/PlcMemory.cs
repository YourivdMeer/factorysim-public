namespace FactorySim.Core.Plc.TestPlc;

/// <summary>
/// The process image of the test PLC, shared by the client threads and the scan thread: coils and holding registers
/// are written by the app, discrete inputs and input registers by the program. Every access takes the lock.
/// </summary>
public sealed class PlcMemory
{
    private readonly object _gate = new();
    private readonly bool[] _coils;
    private readonly bool[] _inputs;
    private readonly ushort[] _holding;
    private readonly ushort[] _inputRegisters;

    public PlcMemory(int size)
    {
        Size = size;
        _coils = new bool[size];
        _inputs = new bool[size];
        _holding = new ushort[size];
        _inputRegisters = new ushort[size];
    }

    /// <summary>Number of coils, discrete inputs and of each kind of register.</summary>
    public int Size { get; }

    public bool Coil(int address)
    {
        lock (_gate)
        {
            return _coils[address];
        }
    }

    public bool[] ReadCoils(int address, int count) => Read(_coils, address, count);

    public bool[] ReadInputs(int address, int count) => Read(_inputs, address, count);

    public ushort[] ReadHolding(int address, int count) => Read(_holding, address, count);

    public ushort[] ReadInputRegisters(int address, int count) => Read(_inputRegisters, address, count);

    public void WriteCoils(int address, IReadOnlyList<bool> values) => Write(_coils, address, values);

    public void WriteHolding(int address, IReadOnlyList<ushort> values) => Write(_holding, address, values);

    /// <summary>Puts outputs of the program from address 0 on.</summary>
    public void SetOutputs(IReadOnlyList<bool> inputs, IReadOnlyList<ushort>? inputRegisters = null)
    {
        Write(_inputs, 0, inputs);
        if (inputRegisters is not null)
        {
            Write(_inputRegisters, 0, inputRegisters);
        }
    }

    /// <summary>One scan under the lock: the program sees the coils and holding registers and sets the outputs.</summary>
    public void Scan(Action<bool[], ushort[], bool[], ushort[]> program)
    {
        lock (_gate)
        {
            program(_coils, _holding, _inputs, _inputRegisters);
        }
    }

    private T[] Read<T>(T[] area, int address, int count)
    {
        lock (_gate)
        {
            return area.AsSpan(address, count).ToArray();
        }
    }

    private void Write<T>(T[] area, int address, IReadOnlyList<T> values)
    {
        lock (_gate)
        {
            for (int index = 0; index < values.Count; index++)
            {
                area[address + index] = values[index];
            }
        }
    }
}
