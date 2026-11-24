namespace FactorySim.Core.Api;

/// <summary>Answer of <c>GET /api/state</c>.</summary>
/// <param name="State"><c>stopped</c>, <c>running</c> or <c>paused</c>.</param>
/// <param name="Scene">Name of the scene that is loaded.</param>
/// <param name="SimTime">Simulation time in seconds.</param>
/// <param name="TimeScale">Simulation seconds per real second.</param>
public sealed record ApiState(string State, string Scene, double SimTime, double TimeScale)
{
    /// <summary>Version of FactorySim (plan 28).</summary>
    public string Version => AppInfo.Version;

    /// <summary>The state as the API spells it.</summary>
    public static string Name(SimState state) => state switch
    {
        SimState.Running => "running",
        SimState.Paused => "paused",
        _ => "stopped",
    };
}
