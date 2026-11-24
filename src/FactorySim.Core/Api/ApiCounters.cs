namespace FactorySim.Core.Api;

/// <summary>Answer of <c>GET /api/counters</c>: the counters of the status bar and the production figures.</summary>
/// <param name="Spawned">Items the emitters made.</param>
/// <param name="Removed">Items per remover, by the remover's name.</param>
/// <param name="InScene">Items in the scene now.</param>
/// <param name="Fallen">Items in the scene that fell off the line.</param>
/// <param name="PerMinute">Removed items per minute over the run time (plan 11).</param>
/// <param name="PerMinuteWindow">Removed items per minute over the last 60 s.</param>
/// <param name="LeadTimeAverage">Average seconds from spawn to removal; null without removed items.</param>
public sealed record ApiCounters(
    int Spawned,
    IReadOnlyDictionary<string, int> Removed,
    int InScene,
    int Fallen,
    double PerMinute,
    double PerMinuteWindow,
    double? LeadTimeAverage);
