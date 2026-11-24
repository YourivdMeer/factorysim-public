using System.Globalization;
using System.Text;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Scenes;

/// <summary>Which tags a tag panel shows.</summary>
public enum TagFilter
{
    All,
    Sensors,
    Actuators,
    Forced,

    /// <summary>Only Int tags. Last, because the filter list in the tag panel follows this order.</summary>
    Analog,
}

/// <summary>
/// One input or output of the PLC and what it is wired to in the scene.
/// </summary>
/// <param name="Tag">The tag with its Modbus and IEC address.</param>
/// <param name="ItemId">The scene item that owns the tag; null for the tags of the simulator itself.</param>
/// <param name="Part">Name of that item, or <see cref="IoList.SimulatorPart"/>.</param>
/// <param name="Signal">What on the part the tag is: "back", "at top", ...; empty when the tag is the part's only or main signal.</param>
/// <param name="AssetName">Library name of the part's asset; null for the simulator.</param>
public sealed record IoPoint(TagMapEntry Tag, string? ItemId, string Part, string Signal, string? AssetName);

/// <summary>
/// The I/O list of a scene: every tag with the part it is connected to. A sensor tag is an
/// input of the PLC, an actuator tag an output.
/// </summary>
public static class IoList
{
    /// <summary>Part name of the tags that belong to the simulator, not to a scene item.</summary>
    public const string SimulatorPart = "Simulator";

    /// <summary>All tags in tag-map order, each with its owner.</summary>
    public static IReadOnlyList<IoPoint> Build(Scene scene, AssetCatalog catalog, IEnumerable<TagMapEntry> entries)
    {
        var owners = new Dictionary<string, (SceneItem Item, CatalogAsset Asset)>(StringComparer.Ordinal);
        foreach (SceneItem item in scene.Items)
        {
            if (catalog.Find(item.Asset) is not { } asset)
            {
                continue;
            }

            foreach (TagDefinition tag in SceneTags.TagsOf(item, asset))
            {
                owners.TryAdd(tag.Name, (item, asset));
            }
        }

        var points = new List<IoPoint>();
        foreach (TagMapEntry entry in entries)
        {
            if (owners.TryGetValue(entry.Name, out (SceneItem Item, CatalogAsset Asset) owner))
            {
                string signal = entry.Name.StartsWith(owner.Item.Name, StringComparison.Ordinal)
                    ? entry.Name[owner.Item.Name.Length..].Trim()
                    : entry.Name;
                points.Add(new IoPoint(entry, owner.Item.Id, owner.Item.Name, signal, owner.Asset.Name));
            }
            else
            {
                points.Add(new IoPoint(entry, null, SimulatorPart, entry.Name, null));
            }
        }

        return points;
    }

    /// <summary>
    /// The tags of one part, found by the id of its scene item, so that a renamed or copied part has its own.
    /// Without an id (null) every tag stays. The tags of the simulator itself belong to no part and drop out.
    /// </summary>
    public static IEnumerable<IoPoint> ForItem(IEnumerable<IoPoint> points, string? itemId) =>
        itemId is null ? points : points.Where(point => point.ItemId == itemId);

    /// <summary>Inputs of the PLC (sensors of the simulator), by address: the bits, then the words.</summary>
    public static IEnumerable<IoPoint> PlcInputs(IEnumerable<IoPoint> points) =>
        points.Where(point => point.Tag.Direction == TagDirection.Sensor).OrderBy(point => point.Tag.Area).ThenBy(point => point.Tag.Offset);

    /// <summary>Outputs of the PLC (actuators of the simulator), by address: the bits, then the words.</summary>
    public static IEnumerable<IoPoint> PlcOutputs(IEnumerable<IoPoint> points) =>
        points.Where(point => point.Tag.Direction == TagDirection.Actuator).OrderBy(point => point.Tag.Area).ThenBy(point => point.Tag.Offset);

    /// <summary>True when a tag passes the filter and the search text (name, part, variable or address).</summary>
    public static bool Matches(IoPoint point, TagFilter filter, string? search, bool forced)
    {
        bool kind = filter switch
        {
            TagFilter.Sensors => point.Tag.Direction == TagDirection.Sensor,
            TagFilter.Actuators => point.Tag.Direction == TagDirection.Actuator,
            TagFilter.Forced => forced,
            TagFilter.Analog => point.Tag.IsAnalog,
            _ => true,
        };
        if (!kind || string.IsNullOrWhiteSpace(search))
        {
            return kind;
        }

        string text = search.Trim();
        return Contains(point.Tag.Name, text) || Contains(point.Part, text) || Contains(point.Tag.Variable, text)
            || Contains(point.Tag.IecAddress, text) || Contains(ModbusText(point.Tag), text);
    }

    /// <summary>"Coil 3", "Discrete input 5", "Holding register 0" or "Input register 1".</summary>
    public static string ModbusText(TagMapEntry tag) => tag.Area switch
    {
        ModbusArea.Coil => "Coil ",
        ModbusArea.DiscreteInput => "Discrete input ",
        ModbusArea.HoldingRegister => "Holding register ",
        _ => "Input register ",
    } + tag.Offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>The IEC type of the PLC variable: BOOL or INT.</summary>
    public static string IecType(TagMapEntry tag) => tag.IsAnalog ? "INT" : "BOOL";

    /// <summary>The I/O list as CSV: inputs first, then outputs.</summary>
    public static string ToCsv(IEnumerable<IoPoint> points)
    {
        IoPoint[] all = points.ToArray();
        var csv = new StringBuilder();
        csv.Append("Kind;IecAddress;Modbus;Variable;Part;Signal;Asset;Type;Unit\r\n");
        foreach ((string kind, IEnumerable<IoPoint> rows) in new[] { ("Input", PlcInputs(all)), ("Output", PlcOutputs(all)) })
        {
            foreach (IoPoint point in rows)
            {
                string[] fields =
                {
                    kind, point.Tag.IecAddress ?? string.Empty, ModbusText(point.Tag), point.Tag.Variable ?? string.Empty,
                    point.Part, point.Signal, point.AssetName ?? string.Empty, IecType(point.Tag), point.Tag.Unit ?? string.Empty,
                };
                csv.Append(string.Join(';', fields.Select(field => field.Replace(';', ',')))).Append("\r\n");
            }
        }

        return csv.ToString();
    }

    private static bool Contains(string? value, string text) =>
        value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
