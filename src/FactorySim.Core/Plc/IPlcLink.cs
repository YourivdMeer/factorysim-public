namespace FactorySim.Core.Plc;

public enum LinkStatus
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>Counters of a running link, for the connection panel and the latency measurement.</summary>
/// <param name="LastExchangeMs">Duration of the last write+read round trip.</param>
/// <param name="AverageExchangeMs">Running average of the round-trip duration.</param>
/// <param name="MaxExchangeMs">Longest round trip since the link started.</param>
/// <param name="LastError">Most recent error message, or null.</param>
public sealed record LinkStatistics(
    long Exchanges,
    long Errors,
    long Reconnects,
    double LastExchangeMs,
    double AverageExchangeMs,
    double MaxExchangeMs,
    string? LastError);

/// <summary>
/// A connection between the tag store and something that plays the PLC.
/// Runs on its own thread; the simulation never waits for it.
/// </summary>
public interface IPlcLink : IDisposable
{
    LinkStatus Status { get; }

    LinkStatistics Statistics { get; }

    /// <summary>Starts exchanging tags. Keeps trying to (re)connect until stopped.</summary>
    void Start();

    /// <summary>Stops the exchange and closes the connection.</summary>
    void Stop();
}
