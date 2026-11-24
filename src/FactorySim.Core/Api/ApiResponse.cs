using System.Text.Json;

namespace FactorySim.Core.Api;

/// <summary>An answer of the test API: a status code and a JSON body.</summary>
public sealed record ApiResponse(int Status, string Json)
{
    /// <summary>camelCase property names; dictionary keys (remover names) stay as they are.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ApiResponse Ok(object body) => new(200, JsonSerializer.Serialize(body, JsonOptions));

    /// <summary>{"error": "…"} with a status code.</summary>
    public static ApiResponse Error(int status, string message) =>
        new(status, JsonSerializer.Serialize(new { error = message }, JsonOptions));

    public static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        413 => "Payload Too Large",
        422 => "Unprocessable Entity",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => "Status",
    };
}
