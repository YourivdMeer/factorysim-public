namespace FactorySim.Core.Tags;

/// <summary>
/// Single source of truth for all tag values, shared by the simulation (main thread)
/// and the PLC link (its own thread).
///
/// The simulation works on one tick at a time: sensor values it writes become visible
/// to the link only at <see cref="EndTick"/>, and actuator values from the link are
/// latched for the simulation at that same moment. Neither side ever sees a half-written tick.
///
/// Bool and Int tags live in separate arrays. The bool calls (<see cref="SensorNames"/>,
/// <see cref="SnapshotSensors"/>, <see cref="ApplyActuators(ReadOnlySpan{bool})"/>) see only the
/// bool tags, exactly as before Int tags existed; the Int tags have their own calls.
/// </summary>
public sealed class TagStore
{
    private readonly object _gate = new();
    private readonly TagDefinition[] _definitions;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    /// <summary>Index within the array of the tag's direction and kind, per tag.</summary>
    private readonly int[] _slot;

    private readonly bool[] _forced;
    private readonly bool[] _forcedValue;
    private readonly int[] _forcedNumber;

    private readonly bool[] _sensorWorking;
    private readonly bool[] _sensorPublished;
    private readonly bool[] _actuatorInbox;
    private readonly bool[] _actuatorLatched;

    private readonly int[] _numberSensorWorking;
    private readonly int[] _numberSensorPublished;
    private readonly int[] _numberActuatorInbox;
    private readonly int[] _numberActuatorLatched;

    /// <summary>Definitions of the Int actuators, in <see cref="AnalogActuatorNames"/> order, for clipping.</summary>
    private readonly TagDefinition[] _numberActuators;

    public TagStore(IEnumerable<TagDefinition> definitions)
    {
        _definitions = definitions.ToArray();
        _slot = new int[_definitions.Length];
        _forced = new bool[_definitions.Length];
        _forcedValue = new bool[_definitions.Length];
        _forcedNumber = new int[_definitions.Length];

        var sensors = new List<string>();
        var actuators = new List<string>();
        var numberSensors = new List<string>();
        var numberActuators = new List<TagDefinition>();
        for (int i = 0; i < _definitions.Length; i++)
        {
            TagDefinition definition = _definitions[i];
            if (!_indexByName.TryAdd(definition.Name, i))
            {
                throw new ArgumentException($"Duplicate tag name '{definition.Name}'.", nameof(definitions));
            }

            if (definition.Kind == TagKind.Int)
            {
                if (definition.Min > definition.Max)
                {
                    throw new ArgumentException($"Tag '{definition.Name}' has a minimum above its maximum.", nameof(definitions));
                }

                if (!(definition.Scale > 0))
                {
                    throw new ArgumentException($"Tag '{definition.Name}' needs a scale above zero.", nameof(definitions));
                }

                if (definition.Direction == TagDirection.Sensor)
                {
                    _slot[i] = numberSensors.Count;
                    numberSensors.Add(definition.Name);
                }
                else
                {
                    _slot[i] = numberActuators.Count;
                    numberActuators.Add(definition);
                }

                continue;
            }

            List<string> list = definition.Direction == TagDirection.Sensor ? sensors : actuators;
            _slot[i] = list.Count;
            list.Add(definition.Name);
        }

        SensorNames = sensors;
        ActuatorNames = actuators;
        AnalogSensorNames = numberSensors;
        AnalogActuatorNames = numberActuators.Select(definition => definition.Name).ToArray();
        _numberActuators = numberActuators.ToArray();
        _sensorWorking = new bool[sensors.Count];
        _sensorPublished = new bool[sensors.Count];
        _actuatorInbox = new bool[actuators.Count];
        _actuatorLatched = new bool[actuators.Count];
        _numberSensorWorking = new int[numberSensors.Count];
        _numberSensorPublished = new int[numberSensors.Count];
        _numberActuatorInbox = new int[numberActuators.Count];
        _numberActuatorLatched = new int[numberActuators.Count];
    }

    /// <summary>Bool sensor tags in definition order; matches <see cref="SnapshotSensors"/>.</summary>
    public IReadOnlyList<string> SensorNames { get; }

    /// <summary>Bool actuator tags in definition order; matches <see cref="ApplyActuators(ReadOnlySpan{bool})"/>.</summary>
    public IReadOnlyList<string> ActuatorNames { get; }

    /// <summary>Int sensor tags in definition order; matches <see cref="SnapshotSensorNumbers"/>.</summary>
    public IReadOnlyList<string> AnalogSensorNames { get; }

    /// <summary>Int actuator tags in definition order; matches <see cref="ApplyActuatorNumbers"/>.</summary>
    public IReadOnlyList<string> AnalogActuatorNames { get; }

    /// <summary>The definition of a tag, or null when there is no such tag.</summary>
    public TagDefinition? Find(string name) => _indexByName.TryGetValue(name, out int index) ? _definitions[index] : null;

    // ---- Simulation side ----

    /// <summary>Writes a bool sensor value for the current tick.</summary>
    public void SetSensor(string name, bool value)
    {
        int index = IndexOf(name, TagDirection.Sensor, TagKind.Bool);
        lock (_gate)
        {
            _sensorWorking[_slot[index]] = value;
        }
    }

    /// <summary>Writes the number of an Int sensor for the current tick, clipped to its range.</summary>
    public void SetSensorNumber(string name, int number)
    {
        int index = IndexOf(name, TagDirection.Sensor, TagKind.Int);
        int clipped = _definitions[index].Clip(number);
        lock (_gate)
        {
            _numberSensorWorking[_slot[index]] = clipped;
        }
    }

    /// <summary>Writes a measured value of an Int sensor: value × scale, rounded and clipped.</summary>
    public void SetSensorValue(string name, double value)
    {
        TagDefinition definition = _definitions[IndexOf(name, TagDirection.Sensor, TagKind.Int)];
        SetSensorNumber(name, ToNumber(value * definition.Scale));
    }

    /// <summary>Reads the bool actuator value to use this tick (forced value while forced).</summary>
    public bool GetActuator(string name)
    {
        int index = IndexOf(name, TagDirection.Actuator, TagKind.Bool);
        lock (_gate)
        {
            return _forced[index] ? _forcedValue[index] : _actuatorLatched[_slot[index]];
        }
    }

    /// <summary>Reads the number of an Int actuator to use this tick (forced number while forced).</summary>
    public int GetActuatorNumber(string name)
    {
        int index = IndexOf(name, TagDirection.Actuator, TagKind.Int);
        lock (_gate)
        {
            return _forced[index] ? _forcedNumber[index] : _numberActuatorLatched[_slot[index]];
        }
    }

    /// <summary>Reads an Int actuator as a value in the simulation: number / scale.</summary>
    public double GetActuatorValue(string name) => GetActuatorNumber(name) / Find(name)!.Scale;

    /// <summary>
    /// Ends the simulation tick: publishes this tick's sensor values to the link and
    /// latches the latest actuator values from the link for the next tick.
    /// </summary>
    public void EndTick()
    {
        lock (_gate)
        {
            PublishLocked();
            Array.Copy(_actuatorInbox, _actuatorLatched, _actuatorInbox.Length);
            Array.Copy(_numberActuatorInbox, _numberActuatorLatched, _numberActuatorInbox.Length);
        }
    }

    /// <summary>Clears all values, for a scene reset. Forces stay in place.</summary>
    public void ResetValues()
    {
        lock (_gate)
        {
            Array.Clear(_sensorWorking);
            Array.Clear(_actuatorInbox);
            Array.Clear(_actuatorLatched);
            Array.Clear(_numberSensorWorking);
            Array.Clear(_numberActuatorInbox);
            Array.Clear(_numberActuatorLatched);
            PublishLocked();
        }
    }

    // ---- Link side ----

    /// <summary>Bool sensor values of the last completed tick, in <see cref="SensorNames"/> order.</summary>
    public bool[] SnapshotSensors()
    {
        lock (_gate)
        {
            return (bool[])_sensorPublished.Clone();
        }
    }

    /// <summary>Int sensor numbers of the last completed tick, in <see cref="AnalogSensorNames"/> order.</summary>
    public int[] SnapshotSensorNumbers()
    {
        lock (_gate)
        {
            return (int[])_numberSensorPublished.Clone();
        }
    }

    /// <summary>Both kinds of sensor of the same completed tick, for the link.</summary>
    public (bool[] Bits, int[] Numbers) SnapshotSensorImage()
    {
        lock (_gate)
        {
            return ((bool[])_sensorPublished.Clone(), (int[])_numberSensorPublished.Clone());
        }
    }

    /// <summary>Stores bool actuator values read from the PLC, in <see cref="ActuatorNames"/> order.</summary>
    public void ApplyActuators(ReadOnlySpan<bool> values)
    {
        CheckLength(values.Length, _actuatorInbox.Length, nameof(values));
        lock (_gate)
        {
            values.CopyTo(_actuatorInbox);
        }
    }

    /// <summary>Stores Int actuator numbers read from the PLC, in <see cref="AnalogActuatorNames"/> order; clipped to each range.</summary>
    public void ApplyActuatorNumbers(ReadOnlySpan<int> numbers)
    {
        CheckLength(numbers.Length, _numberActuatorInbox.Length, nameof(numbers));
        lock (_gate)
        {
            for (int i = 0; i < numbers.Length; i++)
            {
                _numberActuatorInbox[i] = _numberActuators[i].Clip(numbers[i]);
            }
        }
    }

    // ---- UI side ----

    /// <summary>Forces a bool tag.</summary>
    public void Force(string name, bool value)
    {
        int index = IndexOf(name, TagKind.Bool);
        lock (_gate)
        {
            _forced[index] = true;
            _forcedValue[index] = value;
        }
    }

    /// <summary>Forces an Int tag to a number, clipped to its range.</summary>
    public void Force(string name, int number)
    {
        int index = IndexOf(name, TagKind.Int);
        int clipped = _definitions[index].Clip(number);
        lock (_gate)
        {
            _forced[index] = true;
            _forcedNumber[index] = clipped;
        }
    }

    public void Release(string name)
    {
        int index = IndexOf(name);
        lock (_gate)
        {
            _forced[index] = false;
        }
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            Array.Clear(_forced);
        }
    }

    /// <summary>All tags with their current values, in definition order.</summary>
    public IReadOnlyList<TagState> Snapshot()
    {
        var result = new TagState[_definitions.Length];
        lock (_gate)
        {
            for (int i = 0; i < _definitions.Length; i++)
            {
                TagDefinition definition = _definitions[i];
                int slot = _slot[i];
                bool sensor = definition.Direction == TagDirection.Sensor;
                if (definition.Kind == TagKind.Int)
                {
                    int number = sensor ? _numberSensorWorking[slot] : _numberActuatorLatched[slot];
                    result[i] = new TagState(definition.Name, definition.Direction, number != 0, _forced[i], _forcedNumber[i] != 0,
                        TagKind.Int, number, _forcedNumber[i], definition.Scale, definition.Unit);
                }
                else
                {
                    bool value = sensor ? _sensorWorking[slot] : _actuatorLatched[slot];
                    result[i] = new TagState(definition.Name, definition.Direction, value, _forced[i], _forcedValue[i]);
                }
            }
        }

        return result;
    }

    /// <summary>A value as a whole number, rounded half away from zero and kept within an int.</summary>
    private static int ToNumber(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }

    private static void CheckLength(int actual, int expected, string parameter)
    {
        if (actual != expected)
        {
            throw new ArgumentException($"Expected {expected} actuator values, got {actual}.", parameter);
        }
    }

    private void PublishLocked()
    {
        for (int i = 0; i < _definitions.Length; i++)
        {
            TagDefinition definition = _definitions[i];
            if (definition.Direction != TagDirection.Sensor)
            {
                continue;
            }

            int slot = _slot[i];
            if (definition.Kind == TagKind.Int)
            {
                _numberSensorPublished[slot] = _forced[i] ? _forcedNumber[i] : _numberSensorWorking[slot];
            }
            else
            {
                _sensorPublished[slot] = _forced[i] ? _forcedValue[i] : _sensorWorking[slot];
            }
        }
    }

    private int IndexOf(string name)
    {
        if (!_indexByName.TryGetValue(name, out int index))
        {
            throw new KeyNotFoundException($"Unknown tag '{name}'.");
        }

        return index;
    }

    private int IndexOf(string name, TagKind kind)
    {
        int index = IndexOf(name);
        if (_definitions[index].Kind != kind)
        {
            throw new InvalidOperationException($"Tag '{name}' is not a{(kind == TagKind.Int ? "n Int" : " Bool")} tag.");
        }

        return index;
    }

    private int IndexOf(string name, TagDirection expected, TagKind kind)
    {
        int index = IndexOf(name, kind);
        if (_definitions[index].Direction != expected)
        {
            throw new InvalidOperationException($"Tag '{name}' is not a {expected.ToString().ToLowerInvariant()}.");
        }

        return index;
    }
}
