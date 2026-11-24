using FactorySim.Core.Plc;

namespace FactorySim.Core.Api;

/// <summary>Answer of <c>GET /api/plc</c>, as the PLC window shows it.</summary>
/// <param name="Status"><c>disconnected</c>, <c>connecting</c> or <c>connected</c>.</param>
/// <param name="Host">Host of the link, or null without a link.</param>
/// <param name="Port">Port of the link, or null without a link.</param>
public sealed record ApiPlcStatus(
    string Status,
    string? Host,
    int? Port,
    long Exchanges,
    long Errors,
    long Reconnects,
    double AverageExchangeMs,
    double MaxExchangeMs,
    string? LastError)
{
    public static readonly ApiPlcStatus Disconnected = new("disconnected", null, null, 0, 0, 0, 0, 0, null);

    public static ApiPlcStatus Of(LinkStatus status, LinkStatistics stats, string host, int port) => new(
        status switch
        {
            LinkStatus.Connected => "connected",
            LinkStatus.Connecting => "connecting",
            _ => "disconnected",
        },
        host,
        port,
        stats.Exchanges,
        stats.Errors,
        stats.Reconnects,
        stats.AverageExchangeMs,
        stats.MaxExchangeMs,
        stats.LastError);
}
