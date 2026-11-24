namespace FactorySim.Core.Tags;

/// <summary>One change of a tag in a trace.</summary>
/// <param name="Time">Simulation time in seconds.</param>
/// <param name="Tag">Tag name.</param>
/// <param name="Value">The value that is used: 0 or 1 for a bool tag, the number on the line for an Int tag.</param>
/// <param name="Forced">Whether the value was forced.</param>
public readonly record struct TagChange(double Time, string Tag, int Value, bool Forced);
