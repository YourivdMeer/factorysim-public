namespace FactorySim.Core;

/// <summary>On-time of one actuator tag; see <see cref="ProductionStats"/>.</summary>
/// <param name="Tag">Name of the actuator tag.</param>
/// <param name="OnSeconds">Simulation seconds it was on.</param>
/// <param name="OnFraction">Those seconds as a part of the run time, 0–1.</param>
public sealed record ActuatorStats(string Tag, double OnSeconds, double OnFraction);
