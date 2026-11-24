namespace FactorySim.Core;

/// <summary>Figures of one remover at one moment; see <see cref="ProductionStats"/>.</summary>
/// <param name="Name">Name of the remover in the scene.</param>
/// <param name="Count">Items it removed.</param>
/// <param name="PerMinute">Items per minute over the whole run time.</param>
/// <param name="PerMinuteWindow">Items per minute over the last 60 s (over the run time while that is shorter).</param>
/// <param name="LeadTimeCount">Items with a known spawn time, the ones the lead times are about.</param>
/// <param name="LeadTimeAverage">Average seconds from spawn to removal; null without such items.</param>
/// <param name="LeadTimeMin">Shortest lead time in seconds, or null.</param>
/// <param name="LeadTimeMax">Longest lead time in seconds, or null.</param>
public sealed record RemoverStats(
    string Name,
    int Count,
    double PerMinute,
    double PerMinuteWindow,
    int LeadTimeCount,
    double? LeadTimeAverage,
    double? LeadTimeMin,
    double? LeadTimeMax);
