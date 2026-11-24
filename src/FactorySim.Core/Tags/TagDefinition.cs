namespace FactorySim.Core.Tags;

/// <summary>Which way a tag flows, seen from the simulation.</summary>
public enum TagDirection
{
    /// <summary>Simulation to PLC: sensors, buttons, status.</summary>
    Sensor,

    /// <summary>PLC to simulation: motors, cylinders, lamps.</summary>
    Actuator,
}

/// <summary>What a tag carries.</summary>
public enum TagKind
{
    /// <summary>On or off: a BOOL in the PLC, a coil or discrete input on Modbus.</summary>
    Bool,

    /// <summary>A whole number of 16 bits with sign: an INT in the PLC, one Modbus register.</summary>
    Int,
}

/// <summary>Name, direction and kind of one tag.</summary>
/// <param name="Scale">Int tags: number on the line = value in the simulation × scale.</param>
/// <param name="Unit">Int tags: unit of the value in the simulation, for display.</param>
/// <param name="Min">Int tags: lowest number on the line; lower values are clipped.</param>
/// <param name="Max">Int tags: highest number on the line; higher values are clipped.</param>
public sealed record TagDefinition(
    string Name,
    TagDirection Direction,
    TagKind Kind = TagKind.Bool,
    double Scale = 1,
    string? Unit = null,
    int Min = short.MinValue,
    int Max = short.MaxValue)
{
    /// <summary>Keeps a number within the range of the tag and of a 16-bit INT.</summary>
    public int Clip(int number) =>
        Math.Clamp(number, Math.Max(Min, short.MinValue), Math.Min(Max, short.MaxValue));
}

/// <summary>Point-in-time view of a tag, for the tag table.</summary>
/// <param name="Value">Bool tags: the unforced value, what the sensor measures or what the PLC commands.</param>
/// <param name="Number">Int tags: the unforced number on the line.</param>
public sealed record TagState(
    string Name,
    TagDirection Direction,
    bool Value,
    bool Forced,
    bool ForcedValue,
    TagKind Kind = TagKind.Bool,
    int Number = 0,
    int ForcedNumber = 0,
    double Scale = 1,
    string? Unit = null)
{
    /// <summary>The value that is actually used: the forced value while forced, otherwise <see cref="Value"/>.</summary>
    public bool EffectiveValue => Forced ? ForcedValue : Value;

    /// <summary>Int tags: the number that is actually used.</summary>
    public int EffectiveNumber => Forced ? ForcedNumber : Number;
}
