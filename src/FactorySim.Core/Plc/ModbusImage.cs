using FactorySim.Core.Tags;

namespace FactorySim.Core.Plc;

/// <summary>
/// Translates between the tag store's sensor/actuator arrays and the Modbus areas, using the
/// offsets in the tag map: bool tags in coils and discrete inputs, Int tags in holding and input
/// registers. A register holds a 16-bit INT as its two's complement.
/// </summary>
public sealed class ModbusImage
{
    /// <summary>Coil offset per sensor, in <see cref="TagStore.SensorNames"/> order.</summary>
    private readonly int[] _coilOffsets;

    /// <summary>Discrete-input offset per actuator, in <see cref="TagStore.ActuatorNames"/> order.</summary>
    private readonly int[] _inputOffsets;

    /// <summary>Holding-register offset per Int sensor, in <see cref="TagStore.AnalogSensorNames"/> order.</summary>
    private readonly int[] _holdingOffsets;

    /// <summary>Input-register offset per Int actuator, in <see cref="TagStore.AnalogActuatorNames"/> order.</summary>
    private readonly int[] _inputRegisterOffsets;

    public ModbusImage(TagMap map, TagStore store)
    {
        Dictionary<string, int> offsets = map.Entries.ToDictionary(entry => entry.Name, entry => entry.Offset);
        _coilOffsets = store.SensorNames.Select(name => offsets[name]).ToArray();
        _inputOffsets = store.ActuatorNames.Select(name => offsets[name]).ToArray();
        _holdingOffsets = store.AnalogSensorNames.Select(name => offsets[name]).ToArray();
        _inputRegisterOffsets = store.AnalogActuatorNames.Select(name => offsets[name]).ToArray();
        CoilCount = map.CoilCount;
        DiscreteInputCount = map.DiscreteInputCount;
        HoldingRegisterCount = map.HoldingRegisterCount;
        InputRegisterCount = map.InputRegisterCount;
    }

    /// <summary>Number of coils written per exchange, starting at address 0.</summary>
    public int CoilCount { get; }

    /// <summary>Number of discrete inputs read per exchange, starting at address 0.</summary>
    public int DiscreteInputCount { get; }

    /// <summary>Number of holding registers written per exchange, starting at address 0; 0 when the scene has no Int sensors.</summary>
    public int HoldingRegisterCount { get; }

    /// <summary>Number of input registers read per exchange, starting at address 0; 0 when the scene has no Int actuators.</summary>
    public int InputRegisterCount { get; }

    /// <summary>Lays the sensor values out as the coil block to write.</summary>
    public bool[] BuildCoils(IReadOnlyList<bool> sensors)
    {
        var coils = new bool[CoilCount];
        for (int i = 0; i < _coilOffsets.Length; i++)
        {
            coils[_coilOffsets[i]] = sensors[i];
        }

        return coils;
    }

    /// <summary>Picks the actuator values out of the discrete-input block that was read.</summary>
    public bool[] ExtractActuators(IReadOnlyList<bool> discreteInputs)
    {
        if (discreteInputs.Count < DiscreteInputCount)
        {
            throw new ArgumentException(
                $"Expected {DiscreteInputCount} discrete inputs, got {discreteInputs.Count}.", nameof(discreteInputs));
        }

        var actuators = new bool[_inputOffsets.Length];
        for (int i = 0; i < _inputOffsets.Length; i++)
        {
            actuators[i] = discreteInputs[_inputOffsets[i]];
        }

        return actuators;
    }

    /// <summary>Lays the Int sensor numbers out as the holding-register block to write.</summary>
    public ushort[] BuildHoldingRegisters(IReadOnlyList<int> numbers)
    {
        var registers = new ushort[HoldingRegisterCount];
        for (int i = 0; i < _holdingOffsets.Length; i++)
        {
            registers[_holdingOffsets[i]] = unchecked((ushort)(short)Math.Clamp(numbers[i], short.MinValue, short.MaxValue));
        }

        return registers;
    }

    /// <summary>Picks the Int actuator numbers out of the input-register block that was read.</summary>
    public int[] ExtractActuatorNumbers(IReadOnlyList<ushort> inputRegisters)
    {
        if (inputRegisters.Count < InputRegisterCount)
        {
            throw new ArgumentException(
                $"Expected {InputRegisterCount} input registers, got {inputRegisters.Count}.", nameof(inputRegisters));
        }

        var numbers = new int[_inputRegisterOffsets.Length];
        for (int i = 0; i < _inputRegisterOffsets.Length; i++)
        {
            numbers[i] = unchecked((short)inputRegisters[_inputRegisterOffsets[i]]);
        }

        return numbers;
    }
}
