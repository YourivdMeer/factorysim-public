using System.Net;

namespace FactorySim.Core.Plc;

/// <summary>Settings of the Modbus TCP link. Defaults keep all traffic on this machine.</summary>
public sealed record ModbusLinkOptions
{
    /// <summary>Modbus TCP has no authentication, so the default is the loopback address.</summary>
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 502;

    /// <summary>CODESYS V3.5 SP16 and later expect 0 or 255.</summary>
    public byte UnitId { get; init; } = 255;

    /// <summary>Time between two exchanges.</summary>
    public int PollIntervalMs { get; init; } = 10;

    /// <summary>Connect, read and write time-out.</summary>
    public int TimeoutMs { get; init; } = 200;

    /// <summary>Pause before a new connection attempt.</summary>
    public int ReconnectDelayMs { get; init; } = 1000;

    /// <summary>True when the host can only be this machine.</summary>
    public static bool IsLoopback(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim(), out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}
