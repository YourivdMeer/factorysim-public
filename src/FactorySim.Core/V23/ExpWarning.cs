namespace FactorySim.Core.V23;

/// <summary>Something the container parser could not place or found incomplete, at a 1-based line.</summary>
public sealed record ExpWarning(int Line, string Message);
