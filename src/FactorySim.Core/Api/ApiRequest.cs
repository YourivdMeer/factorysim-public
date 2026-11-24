namespace FactorySim.Core.Api;

/// <summary>One HTTP request as the test API reads it.</summary>
/// <param name="Method">GET, POST, PUT, DELETE, … in capitals.</param>
/// <param name="Segments">The path split on '/' first and only then unescaped, so "%2F" never makes a new segment.</param>
/// <param name="Headers">Header names in any case.</param>
public sealed record ApiRequest(
    string Method,
    IReadOnlyList<string> Segments,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;
}
