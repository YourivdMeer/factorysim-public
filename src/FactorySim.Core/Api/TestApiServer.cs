using System.Net;
using System.Net.Sockets;

namespace FactorySim.Core.Api;

/// <summary>
/// The local test API (plan 21): HTTP on 127.0.0.1 only, one token per start, no CORS headers.
/// Accepts on its own thread, like the Modbus link; each connection is answered on the thread pool,
/// at most <see cref="MaxConnections"/> at a time. See docs/SECURITY.md.
/// </summary>
public sealed class TestApiServer : IDisposable
{
    public const int DefaultPort = 5080;

    public const int MaxConnections = 8;

    /// <summary>A client that sends nothing for this long is dropped.</summary>
    public const int ReadTimeoutMs = 3000;

    private readonly ApiRouter _router;
    private readonly string _token;
    private readonly int _requestedPort;
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private TcpListener? _listener;
    private Thread? _acceptThread;

    /// <param name="port">0 lets the system pick a free port; <see cref="Port"/> tells which.</param>
    public TestApiServer(ISimControl control, MainThreadQueue queue, string token, int port = DefaultPort)
    {
        if (token.Length < 16)
        {
            throw new ArgumentException("The token is too short.", nameof(token));
        }

        _router = new ApiRouter(control, queue);
        _token = token;
        _requestedPort = port;
    }

    /// <summary>The address the server listens on, or null when it is stopped.</summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <summary>The port it listens on; the requested port while stopped.</summary>
    public int Port => LocalEndpoint?.Port ?? _requestedPort;

    public bool IsRunning => _listener is not null;

    /// <summary>Starts listening on 127.0.0.1. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.Loopback, _requestedPort) { ExclusiveAddressUse = true };
        listener.Start();
        _listener = listener;
        _acceptThread = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "Test API" };
        _acceptThread.Start();
    }

    /// <summary>Stops listening and frees the port. Requests being answered finish on their own.</summary>
    public void Stop()
    {
        TcpListener? listener = _listener;
        if (listener is null)
        {
            return;
        }

        _listener = null;
        listener.Stop();
        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _acceptThread = null;
    }

    public void Dispose() => Stop();

    private void AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                return;                                 // stopped
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!_slots.Wait(0))
            {
                client.Dispose();                       // too many at once
                continue;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Serve(client);
                }
                finally
                {
                    _slots.Release();
                }
            });
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = ReadTimeoutMs;
                client.SendTimeout = ReadTimeoutMs;
                NetworkStream stream = client.GetStream();
                ApiResponse response;
                bool readAll = true;
                try
                {
                    response = Answer(HttpWire.Read(stream));
                }
                catch (HttpWireException e)
                {
                    response = ApiResponse.Error(e.Status, e.Message);
                    readAll = false;
                }

                HttpWire.Write(stream, response);
                if (!readAll)
                {
                    Drain(client);
                }
            }
            catch (IOException)
            {
                // The client went away or stayed silent; nothing to answer.
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                try
                {
                    HttpWire.Write(client.GetStream(), ApiResponse.Error(500, e.Message));
                }
                catch (Exception)
                {
                    // Best effort only.
                }
            }
        }
    }

    /// <summary>Checks where the request comes from and the token, then routes it.</summary>
    private ApiResponse Answer(ApiRequest request)
    {
        // A browser always sends Origin with a request from another site; scripts do not.
        if (request.Header("Origin") is not null)
        {
            return ApiResponse.Error(403, "Requests from a web page are not accepted.");
        }

        // Against DNS rebinding: the name in the address bar must be this machine.
        if (!IsLocalHost(request.Header("Host")))
        {
            return ApiResponse.Error(403, "Host must be 127.0.0.1 or localhost.");
        }

        if (!ApiToken.Matches(_token, request.Header(ApiToken.HeaderName)))
        {
            return ApiResponse.Error(401, $"Missing or wrong {ApiToken.HeaderName} header.");
        }

        return _router.Handle(request);
    }

    private static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        string name = host;
        if (name.StartsWith('['))
        {
            int close = name.IndexOf(']');
            name = close < 0 ? name : name[..(close + 1)];
        }
        else
        {
            int colon = name.LastIndexOf(':');
            name = colon < 0 ? name : name[..colon];
        }

        return name is "127.0.0.1" or "[::1]" || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>After an early answer: stop sending, read what is left for a moment, so the client gets the answer and not a reset.</summary>
    private static void Drain(TcpClient client)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
            client.ReceiveTimeout = 500;
            var sink = new byte[4096];
            int total = 0;
            int read;
            while (total < 1024 * 1024 && (read = client.Client.Receive(sink)) > 0)
            {
                total += read;
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
