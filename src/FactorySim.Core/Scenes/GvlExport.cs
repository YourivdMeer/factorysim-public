using System.Text;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Scenes;

/// <summary>
/// The I/O list of a scene as a Global Variable List named <c>IO</c>, in Structured Text: one BOOL per bool
/// tag and one INT per Int tag, the inputs of the PLC (sensors: coils, then holding registers) first and then
/// its outputs (actuators: discrete inputs, then input registers), each by address. The Modbus address, the IEC address and what the tag is wired to stand in a comment.
/// There are no <c>AT</c> addresses: in the acceptance project the link runs through the I/O mapping of the
/// Modbus TCP Slave Device. Own text, not a CODESYS format.
/// </summary>
public static class GvlExport
{
    private const string Prefix = "IO.";

    /// <param name="points">The I/O list of the scene, see <see cref="IoList.Build"/>.</param>
    /// <param name="sceneName">Name of the scene, for the comment at the top.</param>
    public static string ToSt(IEnumerable<IoPoint> points, string sceneName)
    {
        IoPoint[] all = points.ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(IoPoint Point, string Variable, string Area)>();
        foreach (IoPoint point in IoList.PlcInputs(all).Concat(IoList.PlcOutputs(all)))
        {
            rows.Add((point, Unique(VariableOf(point), used), AreaName(point.Tag.Area)));
        }

        int width = rows.Count == 0 ? 0 : rows.Max(row => row.Variable.Length);
        var text = new StringBuilder();
        text.Append("(* Global Variable List \"IO\" — gemaakt door FactorySim ").Append(AppInfo.Version).Append(" uit de scène \"")
            .Append(sceneName.Replace("*)", "* )")).Append("\". Eigen tekst.\r\n");
        text.Append("   Plak dit in een GVL met de naam IO. Zonder AT-adressen: de koppeling loopt via de I/O mapping\r\n");
        text.Append("   van het Modbus TCP Slave Device (coils zijn ingangen van de PLC, discrete inputs zijn uitgangen). *)\r\n");
        text.Append("VAR_GLOBAL\r\n");

        string? current = null;
        foreach ((IoPoint point, string variable, string area) in rows)
        {
            if (area != current)
            {
                if (current is not null)
                {
                    text.Append("\r\n");
                }

                string from = point.Tag.Direction == TagDirection.Sensor ? "App -> PLC: " : "PLC -> App: ";
                text.Append("    (* ").Append(from).Append(area).Append("s *)\r\n");
                current = area;
            }

            string address = point.Tag.IecAddress is { Length: > 0 } iec ? ", " + iec : string.Empty;
            string wired = point.Part == point.Tag.Name ? point.Tag.Name : $"{point.Tag.Name} — {point.Part}";
            if (point.AssetName is not null)
            {
                wired += $" ({point.AssetName})";
            }

            text.Append("    ").Append(variable.PadRight(width)).Append(" : ").Append(IoList.IecType(point.Tag)).Append(";    (* ")
                .Append(area).Append(' ').Append(point.Tag.Offset).Append(address).Append(": ")
                .Append(wired.Replace("*)", "* )")).Append(" *)\r\n");
        }

        text.Append("END_VAR\r\n");
        return text.ToString();
    }

    private static string AreaName(ModbusArea area) => area switch
    {
        ModbusArea.Coil => "Coil",
        ModbusArea.DiscreteInput => "Discrete Input",
        ModbusArea.HoldingRegister => "Holding Register",
        _ => "Input Register",
    };

    /// <summary>The variable of a tag without the list name; made from the tag name when the tag has none.</summary>
    private static string VariableOf(IoPoint point)
    {
        string? given = point.Tag.Variable;
        if (!string.IsNullOrWhiteSpace(given))
        {
            return given.StartsWith(Prefix, StringComparison.Ordinal) ? given[Prefix.Length..] : given;
        }

        var name = new StringBuilder(point.Tag.Direction == TagDirection.Sensor ? "i" : "o");
        bool upper = true;
        foreach (char c in point.Tag.Name)
        {
            if (char.IsLetterOrDigit(c) && c < 128)
            {
                name.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        return name.ToString();
    }

    private static string Unique(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int number = 2; !used.Add(candidate); number++)
        {
            candidate = name + number;
        }

        return candidate;
    }
}
