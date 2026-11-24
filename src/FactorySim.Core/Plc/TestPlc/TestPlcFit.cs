using FactorySim.Core.Tags;

namespace FactorySim.Core.Plc.TestPlc;

/// <summary>
/// Whether a tag map fits the <see cref="SorterProgram"/> (plan 34): the seven sensors and four actuators of the
/// MVP scene, each by name (case does not matter) on the address the sorter reads or writes. Extra tags are fine.
/// </summary>
public sealed record TestPlcFit(IReadOnlyList<string> Problems)
{
    private static readonly (string Name, ModbusArea Area, int Offset)[] Required =
    {
        ("Start button", ModbusArea.Coil, SorterProgram.StartCoil),
        ("Stop button", ModbusArea.Coil, SorterProgram.StopCoil),
        ("At pusher", ModbusArea.Coil, SorterProgram.AtPusherCoil),
        ("High box", ModbusArea.Coil, SorterProgram.HighBoxCoil),
        ("Pusher back", ModbusArea.Coil, SorterProgram.PusherBackCoil),
        ("Pusher front", ModbusArea.Coil, SorterProgram.PusherFrontCoil),
        ("Sim running", ModbusArea.Coil, SorterProgram.SimRunningCoil),
        ("Conveyor", ModbusArea.DiscreteInput, SorterProgram.ConveyorOutput),
        ("Pusher", ModbusArea.DiscreteInput, SorterProgram.PusherOutput),
        ("Emitter", ModbusArea.DiscreteInput, SorterProgram.EmitterOutput),
        ("Run lamp", ModbusArea.DiscreteInput, SorterProgram.RunLampOutput),
    };

    /// <summary>True when nothing is missing or misplaced.</summary>
    public bool Fits => Problems.Count == 0;

    /// <summary>One Dutch sentence for a tooltip; empty when the map fits.</summary>
    public string Reason => Fits
        ? string.Empty
        : "De test-PLC past alleen bij de MVP-scène (sorteren op hoogte). Deze scène wijkt af: "
            + string.Join("; ", Problems.Take(4))
            + (Problems.Count > 4 ? $"; en nog {Problems.Count - 4}" : string.Empty) + ".";

    public static TestPlcFit Check(IEnumerable<TagMapEntry> entries)
    {
        var list = entries.ToArray();
        var problems = new List<string>();
        foreach ((string name, ModbusArea area, int offset) in Required)
        {
            TagMapEntry? entry = list.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                problems.Add($"mist {name}");
            }
            else if (entry.Area != area || entry.Offset != offset)
            {
                problems.Add($"{name} staat op {Short(entry.Area)} {entry.Offset} in plaats van {Short(area)} {offset}");
            }
        }

        return new TestPlcFit(problems);
    }

    private static string Short(ModbusArea area) => area switch
    {
        ModbusArea.Coil => "coil",
        ModbusArea.DiscreteInput => "discrete input",
        ModbusArea.HoldingRegister => "holding register",
        _ => "input register",
    };
}
