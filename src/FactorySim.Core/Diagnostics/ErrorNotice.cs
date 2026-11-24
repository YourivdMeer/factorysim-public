namespace FactorySim.Core.Diagnostics;

/// <summary>Plan 31: the newest error and how often it came in a row.</summary>
public sealed record ErrorNotice(string Message, int Count);
