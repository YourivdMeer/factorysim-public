using System.Text;

namespace FactorySim.Core.Api;

/// <summary>
/// The little HTTP/1.1 the test API needs: one request per connection, a body only with
/// Content-Length, no chunked bodies, no keep-alive. Sizes are capped.
/// </summary>
public static class HttpWire
{
    public const int MaxHeaderBytes = 8 * 1024;

    public const int MaxBodyBytes = 64 * 1024;

    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

    /// <summary>Reads one request. Throws <see cref="HttpWireException"/> for anything it does not accept.</summary>
    public static ApiRequest Read(Stream stream)
    {
        var buffer = new byte[MaxHeaderBytes];
        int filled = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, filled, HeaderEnd)) < 0)
        {
            if (filled == buffer.Length)
            {
                throw new HttpWireException(431, "Headers too large.");
            }

            int read = stream.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                throw new HttpWireException(400, "Incomplete request.");
            }

            filled += read;
        }

        string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        string[] lines = head.Split("\r\n");
        string[] start = lines[0].Split(' ');
        if (start.Length != 3 || !start[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || start[0].Length == 0
            || !start[0].All(char.IsAsciiLetterUpper) || !start[1].StartsWith('/'))
        {
            throw new HttpWireException(400, "Malformed request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new HttpWireException(400, "Malformed header.");
            }

            string name = line[..colon].Trim();
            if (!headers.TryAdd(name, line[(colon + 1)..].Trim()))
            {
                throw new HttpWireException(400, $"Header '{name}' appears twice.");
            }
        }

        if (headers.ContainsKey("Transfer-Encoding"))
        {
            throw new HttpWireException(400, "Chunked bodies are not supported; send Content-Length.");
        }

        int length = 0;
        if (headers.TryGetValue("Content-Length", out string? lengthText)
            && (!int.TryParse(lengthText, System.Globalization.NumberStyles.None, null, out length) || length < 0))
        {
            throw new HttpWireException(400, "Bad Content-Length.");
        }

        if (length > MaxBodyBytes)
        {
            throw new HttpWireException(413, "Body too large.");
        }

        var body = new byte[length];
        int already = Math.Min(filled - headerEnd - HeaderEnd.Length, length);
        Array.Copy(buffer, headerEnd + HeaderEnd.Length, body, 0, already);
        while (already < length)
        {
            int read = stream.Read(body, already, length - already);
            if (read == 0)
            {
                throw new HttpWireException(400, "Incomplete body.");
            }

            already += read;
        }

        return new ApiRequest(start[0], Segments(start[1]), headers, body);
    }

    /// <summary>Writes the answer with Connection: close and no CORS headers.</summary>
    public static void Write(Stream stream, ApiResponse response)
    {
        byte[] body = Encoding.UTF8.GetBytes(response.Json);
        string head = $"HTTP/1.1 {response.Status} {ApiResponse.Reason(response.Status)}\r\n"
            + "Content-Type: application/json; charset=utf-8\r\n"
            + $"Content-Length: {body.Length}\r\n"
            + "Cache-Control: no-store\r\n"
            + "Connection: close\r\n\r\n";
        stream.Write(Encoding.ASCII.GetBytes(head));
        stream.Write(body);
        stream.Flush();
    }

    /// <summary>"/api/tags/Pusher%20back?x=1" → ["api", "tags", "Pusher back"]: split, then unescape.</summary>
    public static IReadOnlyList<string> Segments(string target)
    {
        int query = target.IndexOfAny(new[] { '?', '#' });
        string path = query < 0 ? target : target[..query];
        try
        {
            return path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        }
        catch (UriFormatException)
        {
            throw new HttpWireException(400, "Malformed path.");
        }
    }

    private static int IndexOf(byte[] buffer, int count, byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= count; i++)
        {
            if (buffer.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }
}
