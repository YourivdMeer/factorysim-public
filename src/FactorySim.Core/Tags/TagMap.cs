using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactorySim.Core.Tags;

/// <summary>Modbus data area a tag lives in, as seen by the Modbus client (the app).</summary>
public enum ModbusArea
{
    /// <summary>Written by the app; the PLC program reads these as inputs.</summary>
    Coil,

    /// <summary>Read by the app; the PLC program writes these as outputs.</summary>
    DiscreteInput,

    /// <summary>Int tags written by the app (FC16); the PLC program reads these as input words.</summary>
    HoldingRegister,

    /// <summary>Int tags read by the app (FC04); the PLC program writes these as output words.</summary>
    InputRegister,
}

/// <summary>One row of the tag table: where a tag lives on the PLC side.</summary>
/// <param name="IecAddress">Direct address for the embedded runtime (route B), e.g. %IX0.2.</param>
/// <param name="Variable">Name of the PLC variable, for documentation and later import.</param>
/// <param name="Kind">Int for an analog tag; null (or Bool) for a digital one, so older files stay as they are.</param>
/// <param name="Scale">Int tags: number on the line = value in the simulation × scale; null is 1.</param>
/// <param name="Unit">Int tags: unit of the value in the simulation.</param>
/// <param name="Min">Int tags: lowest number on the line; null is −32768.</param>
/// <param name="Max">Int tags: highest number on the line; null is 32767.</param>
public sealed record TagMapEntry(
    string Name,
    TagDirection Direction,
    ModbusArea Area,
    int Offset,
    string? IecAddress = null,
    string? Variable = null,
    string? Description = null,
    TagKind? Kind = null,
    double? Scale = null,
    string? Unit = null,
    int? Min = null,
    int? Max = null)
{
    /// <summary>True for an Int tag.</summary>
    [JsonIgnore]
    public bool IsAnalog => Kind == TagKind.Int;

    /// <summary>The tag as the store needs it.</summary>
    public TagDefinition ToDefinition() => new(
        Name, Direction, Kind ?? TagKind.Bool, Scale ?? 1, Unit, Min ?? short.MinValue, Max ?? short.MaxValue);
}

public sealed class TagMapException : Exception
{
    public TagMapException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The mapping between scene tags and PLC addresses. Loaded from JSON, exported to CSV.</summary>
public sealed class TagMap
{
    private const char CsvSeparator = ';';

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public TagMap(IEnumerable<TagMapEntry> entries)
    {
        Entries = entries.ToArray();
        Validate();
    }

    public IReadOnlyList<TagMapEntry> Entries { get; }

    /// <summary>Number of coils the PLC must provide (highest offset + 1).</summary>
    public int CoilCount => CountOf(ModbusArea.Coil);

    /// <summary>Number of discrete inputs the PLC must provide (highest offset + 1).</summary>
    public int DiscreteInputCount => CountOf(ModbusArea.DiscreteInput);

    /// <summary>Number of holding registers the PLC must provide (highest offset + 1).</summary>
    public int HoldingRegisterCount => CountOf(ModbusArea.HoldingRegister);

    /// <summary>Number of input registers the PLC must provide (highest offset + 1).</summary>
    public int InputRegisterCount => CountOf(ModbusArea.InputRegister);

    /// <summary>The area a tag belongs in: bits in coils and discrete inputs, numbers in registers.</summary>
    public static ModbusArea AreaFor(TagDirection direction, TagKind kind) => (direction, kind) switch
    {
        (TagDirection.Sensor, TagKind.Int) => ModbusArea.HoldingRegister,
        (TagDirection.Actuator, TagKind.Int) => ModbusArea.InputRegister,
        (TagDirection.Sensor, _) => ModbusArea.Coil,
        _ => ModbusArea.DiscreteInput,
    };

    public static TagMap FromJson(string json)
    {
        TagMapFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TagMapFile>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new TagMapException($"Tag map is not valid JSON: {e.Message}", e);
        }

        if (file?.Tags is null)
        {
            throw new TagMapException("Tag map has no \"tags\" list.");
        }

        return new TagMap(file.Tags);
    }

    public string ToJson() => JsonSerializer.Serialize(new TagMapFile(Entries.ToArray()), JsonOptions);

    /// <summary>Semicolon-separated export of the mapping, one line per tag.</summary>
    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.Append("Name;Direction;Area;Offset;IecAddress;Variable;Description;Kind;Scale;Unit;Min;Max\r\n");
        foreach (TagMapEntry entry in Entries)
        {
            string[] fields =
            {
                entry.Name,
                entry.Direction.ToString(),
                entry.Area.ToString(),
                entry.Offset.ToString(CultureInfo.InvariantCulture),
                entry.IecAddress ?? string.Empty,
                entry.Variable ?? string.Empty,
                entry.Description ?? string.Empty,
                (entry.Kind ?? TagKind.Bool).ToString(),
                entry.Scale?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.Unit ?? string.Empty,
                entry.Min?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.Max?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            };
            csv.AppendJoin(CsvSeparator, fields.Select(Escape)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>Tag definitions for a <see cref="TagStore"/>, in entry order.</summary>
    public IEnumerable<TagDefinition> ToDefinitions() => Entries.Select(entry => entry.ToDefinition());

    private static string Escape(string field)
    {
        bool needsQuotes = field.Contains(CsvSeparator) || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        return needsQuotes ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    private int CountOf(ModbusArea area) =>
        Entries.Where(entry => entry.Area == area).Select(entry => entry.Offset + 1).DefaultIfEmpty(0).Max();

    private void Validate()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<(ModbusArea, int)>();
        foreach (TagMapEntry entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new TagMapException("A tag has no name.");
            }

            if (!names.Add(entry.Name))
            {
                throw new TagMapException($"Tag '{entry.Name}' is listed more than once.");
            }

            if (entry.Offset < 0)
            {
                throw new TagMapException($"Tag '{entry.Name}' has a negative offset.");
            }

            ModbusArea expected = AreaFor(entry.Direction, entry.Kind ?? TagKind.Bool);
            if (entry.Area != expected)
            {
                string what = entry.IsAnalog ? "an Int " + entry.Direction : "a " + entry.Direction;
                throw new TagMapException(
                    $"Tag '{entry.Name}' is {what} and must be mapped to {expected}, not {entry.Area}.");
            }

            if (entry.Min > entry.Max)
            {
                throw new TagMapException($"Tag '{entry.Name}' has a minimum above its maximum.");
            }

            if (!addresses.Add((entry.Area, entry.Offset)))
            {
                throw new TagMapException($"{entry.Area} {entry.Offset} is used by more than one tag.");
            }
        }
    }

    private sealed record TagMapFile(TagMapEntry[]? Tags);
}
