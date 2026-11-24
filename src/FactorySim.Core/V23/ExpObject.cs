namespace FactorySim.Core.V23;

/// <summary>
/// One top-level object of a <c>.exp</c> file, as the container parser found it. Lines are
/// 1-based and include the <c>(* @… *)</c> metadata lines in front of the object.
/// </summary>
public sealed record ExpObject
{
    public required ExpObjectKind Kind { get; init; }

    /// <summary>The POU, type or list name; the file name for a library; the opening keyword otherwise.</summary>
    public required string Name { get; init; }

    /// <summary>The folder in the object organizer from <c>@PATH</c>, with <c>/</c> between folders; empty at the top.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The <c>(* @KEY := 'value' *)</c> lines in front of the object, without the quotes.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public required int FirstLine { get; init; }

    public required int LastLine { get; init; }

    /// <summary>The whole object, metadata included, with <c>\n</c> between lines.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// For a POU: the header and the <c>VAR</c> blocks up to <c>@END_DECLARATION</c>. For a type: <c>TYPE … END_TYPE</c>.
    /// For a variable list: its <c>VAR_…</c> blocks. Empty otherwise.
    /// </summary>
    public string Declaration { get; init; } = string.Empty;

    /// <summary>For a POU: everything between <c>@END_DECLARATION</c> and <c>END_…</c>. Empty otherwise.</summary>
    public string Body { get; init; } = string.Empty;

    public ExpBodyKind BodyKind { get; init; } = ExpBodyKind.None;
}
