namespace FactorySim.Core.Api;

/// <summary>A request that cannot be read; the server answers with <see cref="Status"/> and closes.</summary>
public sealed class HttpWireException : Exception
{
    public HttpWireException(int status, string message)
        : base(message) => Status = status;

    public int Status { get; }
}
