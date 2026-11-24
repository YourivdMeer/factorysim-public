using System.Text.RegularExpressions;

namespace FactorySim.Core.V23;

/// <summary>
/// Splits a CoDeSys V2.3 <c>.exp</c> export into its top-level objects (spoor B, B0). It does not
/// parse declarations or code; unknown blocks are kept as <see cref="ExpObjectKind.Unknown"/>
/// and reported. The layout comes from real exports.
/// </summary>
public sealed class ExpContainer
{
    private static readonly Regex MetadataLine = new(@"^\(\*\s*@(\w+)\s*:=\s*(.*?)\s*\*\)$", RegexOptions.Compiled);
    private static readonly Regex PouHeader = new(@"^(PROGRAM|FUNCTION_BLOCK|FUNCTION)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);
    private static readonly Regex TypeHeader = new(@"^TYPE\s+([A-Za-z_]\w*)", RegexOptions.Compiled);
    private static readonly Regex UnknownBodyMarker = new(@"^_\w+_BODY$", RegexOptions.Compiled);
    private static readonly Regex FirstWord = new(@"^\w+", RegexOptions.Compiled);

    /// <summary>Blocks that open with a keyword on its own line and close with another.</summary>
    private static readonly Dictionary<string, (ExpObjectKind Kind, string End)> KeywordBlocks = new()
    {
        ["LIBRARY"] = (ExpObjectKind.Library, "END_LIBRARY"),
        ["PLC_CONFIGURATION"] = (ExpObjectKind.PlcConfiguration, "PLC_END"),
        ["RESOURCE"] = (ExpObjectKind.Resource, "END_RESOURCE"),
        ["_ALARMCONFIG"] = (ExpObjectKind.AlarmConfiguration, "_END_ALARMCONFIG"),
        ["_WORKSPACE"] = (ExpObjectKind.Workspace, "_END_WORKSPACE"),
    };

    private readonly string[] _lines;
    private readonly int _lastContent;
    private readonly List<ExpObject> _objects = new();
    private readonly List<ExpWarning> _warnings = new();

    private ExpContainer(string text)
    {
        _lines = text.Replace("\r\n", "\n").Split('\n');
        _lastContent = Array.FindLastIndex(_lines, line => line.Trim().Length > 0);
        Split();
    }

    public IReadOnlyList<ExpObject> Objects => _objects;

    public IReadOnlyList<ExpWarning> Warnings => _warnings;

    public static ExpContainer Parse(string text) => new(text);

    private void Split()
    {
        var metadata = new Dictionary<string, string>();
        int metadataStart = -1;
        int i = 0;
        while (i < _lines.Length)
        {
            string line = _lines[i].Trim();
            if (line.Length == 0)
            {
                i++;
                continue;
            }

            if (MetadataLine.Match(line) is { Success: true } meta)
            {
                if (metadataStart < 0)
                {
                    metadataStart = i;
                }

                metadata[meta.Groups[1].Value] = Unquote(meta.Groups[2].Value);
                i++;
                continue;
            }

            int start = metadataStart >= 0 ? metadataStart : i;
            ExpObject found = ReadObject(i, line, metadata);
            _objects.Add(found with
            {
                FirstLine = start + 1,
                Path = metadata.TryGetValue("PATH", out string? path) ? path.Replace("\\/", "/") : string.Empty,
                Metadata = metadata,
                Text = Join(start, found.LastLine - 1),
            });
            i = found.LastLine;
            metadata = new Dictionary<string, string>();
            metadataStart = -1;
        }

        if (metadataStart >= 0)
        {
            Warn(metadataStart, "Metadata zonder object aan het eind van het bestand");
        }
    }

    /// <summary>Reads the object whose first line (after its metadata) is at <paramref name="i"/>; LastLine is 1-based.</summary>
    private ExpObject ReadObject(int i, string line, IReadOnlyDictionary<string, string> metadata)
    {
        if (PouHeader.Match(line) is { Success: true } pou)
        {
            return ReadPou(i, pou.Groups[1].Value, pou.Groups[2].Value);
        }

        if (TypeHeader.Match(line) is { Success: true } type)
        {
            return ReadType(i, type.Groups[1].Value);
        }

        if (line.StartsWith("VAR_GLOBAL", StringComparison.Ordinal) || line.StartsWith("VAR_CONFIG", StringComparison.Ordinal))
        {
            return ReadVariableList(i, line.StartsWith("VAR_CONFIG", StringComparison.Ordinal), metadata);
        }

        if (KeywordBlocks.TryGetValue(line, out var block))
        {
            int end = FindEnd(i, block.End, line);
            string name = block.Kind == ExpObjectKind.Library ? LibraryName(i, end) : line;
            return new ExpObject { Kind = block.Kind, Name = name, FirstLine = i + 1, LastLine = end + 1 };
        }

        return ReadUnknown(i, line);
    }

    private ExpObject ReadPou(int i, string keyword, string name)
    {
        string endKeyword = "END_" + keyword;
        int end = FindEnd(i, endKeyword, keyword + " " + name);
        int bodyEnd = _lines[end].Trim() == endKeyword ? end - 1 : end;
        int split = Enumerable.Range(i + 1, Math.Max(0, bodyEnd - i))
            .FirstOrDefault(k => IsMetadata(k, "END_DECLARATION"), -1);
        if (split < 0)
        {
            Warn(i, $"{keyword} {name} heeft geen @END_DECLARATION; de body is niet gevonden");
        }

        string body = split < 0 ? string.Empty : Join(split + 1, bodyEnd);
        return new ExpObject
        {
            Kind = keyword switch
            {
                "PROGRAM" => ExpObjectKind.Program,
                "FUNCTION_BLOCK" => ExpObjectKind.FunctionBlock,
                _ => ExpObjectKind.Function,
            },
            Name = name,
            FirstLine = i + 1,
            LastLine = end + 1,
            Declaration = Join(i, split < 0 ? bodyEnd : split - 1),
            Body = body,
            BodyKind = BodyKindOf(body),
        };
    }

    private ExpObject ReadType(int i, string name)
    {
        int end = FindEnd(i, "END_TYPE", "TYPE " + name);
        int last = end;
        int next = NextContent(end + 1);
        if (next >= 0 && IsMetadata(next, "END_DECLARATION"))
        {
            last = next;
        }

        return new ExpObject { Kind = ExpObjectKind.Type, Name = name, FirstLine = i + 1, LastLine = last + 1, Declaration = Join(i, end) };
    }

    /// <summary>One or more <c>VAR_…</c> blocks, then <c>@OBJECT_END</c> and the <c>@CONNECTIONS</c> comment when they are there.</summary>
    private ExpObject ReadVariableList(int i, bool configuration, IReadOnlyDictionary<string, string> metadata)
    {
        int end = i;
        int k = i;
        while (k >= 0 && _lines[k].Trim().StartsWith("VAR", StringComparison.Ordinal))
        {
            end = FindEnd(k, "END_VAR", _lines[k].Trim());
            k = NextContent(end + 1);
        }

        int declarationEnd = end;
        string? objectEnd = null;
        if (k >= 0 && MetadataLine.Match(_lines[k].Trim()) is { Success: true } meta && meta.Groups[1].Value == "OBJECT_END")
        {
            objectEnd = Unquote(meta.Groups[2].Value);
            end = k;
            int connections = NextContent(k + 1);
            if (connections >= 0 && _lines[connections].Trim().StartsWith("(* @CONNECTIONS", StringComparison.Ordinal))
            {
                end = FindEnd(connections, "*)", "@CONNECTIONS");
            }
        }

        string fallback = configuration ? "VAR_CONFIG" : "VAR_GLOBAL";
        return new ExpObject
        {
            Kind = configuration ? ExpObjectKind.VariableConfiguration : ExpObjectKind.GlobalVariableList,
            Name = metadata.TryGetValue("GLOBAL_VARIABLE_LIST", out string? name) ? name : objectEnd ?? fallback,
            FirstLine = i + 1,
            LastLine = end + 1,
            Declaration = Join(i, declarationEnd),
        };
    }

    /// <summary>
    /// A block the parser does not know. <c>_NAME</c> runs to <c>_END_NAME</c>, a word to <c>END_word</c>;
    /// without that end it is one line.
    /// </summary>
    private ExpObject ReadUnknown(int i, string line)
    {
        string name = FirstWord.Match(line).Value;
        if (name.Length == 0)
        {
            name = line;
        }

        string endKeyword = name.StartsWith('_') ? "_END" + name : "END_" + name;
        int end = Enumerable.Range(i + 1, Math.Max(0, _lines.Length - i - 1))
            .FirstOrDefault(k => _lines[k].Trim() == endKeyword, i);
        Warn(i, $"Onbekend blok {name} overgeslagen (regel {i + 1}–{end + 1})");
        return new ExpObject { Kind = ExpObjectKind.Unknown, Name = name, FirstLine = i + 1, LastLine = end + 1 };
    }

    /// <summary>The index of the first line after <paramref name="i"/> that is <paramref name="endKeyword"/>; without one, the last line with content.</summary>
    private int FindEnd(int i, string endKeyword, string what)
    {
        for (int k = i + 1; k < _lines.Length; k++)
        {
            if (_lines[k].Trim() == endKeyword)
            {
                return k;
            }
        }

        Warn(i, $"{what} heeft geen {endKeyword}; het blok loopt door tot het eind van het bestand");
        return Math.Max(i, _lastContent);
    }

    private string LibraryName(int i, int end)
    {
        int next = NextContent(i + 1);
        if (next < 0 || next >= end)
        {
            return "LIBRARY";
        }

        return _lines[next].Trim().Split(' ', 2)[0];
    }

    private static ExpBodyKind BodyKindOf(string body)
    {
        string first = body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        return first switch
        {
            "_LD_BODY" => ExpBodyKind.Ld,
            "_FBD_BODY" => ExpBodyKind.Fbd,
            _ when first.StartsWith("INITIAL_STEP", StringComparison.Ordinal) => ExpBodyKind.Sfc,
            _ when UnknownBodyMarker.IsMatch(first) => ExpBodyKind.Unknown,
            _ => ExpBodyKind.Text,
        };
    }

    private bool IsMetadata(int k, string key) =>
        MetadataLine.Match(_lines[k].Trim()) is { Success: true } meta && meta.Groups[1].Value == key;

    /// <summary>The index of the first line with content from <paramref name="from"/> on, or -1.</summary>
    private int NextContent(int from)
    {
        for (int k = from; k < _lines.Length; k++)
        {
            if (_lines[k].Trim().Length > 0)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>Lines <paramref name="from"/> to <paramref name="to"/> (inclusive, 0-based) with <c>\n</c>, without blank lines at either end.</summary>
    private string Join(int from, int to)
    {
        while (from <= to && _lines[from].Trim().Length == 0)
        {
            from++;
        }

        while (to >= from && _lines[to].Trim().Length == 0)
        {
            to--;
        }

        return from > to ? string.Empty : string.Join("\n", _lines[from..(to + 1)]);
    }

    private void Warn(int i, string message) => _warnings.Add(new ExpWarning(i + 1, message));

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1] : value;
}
