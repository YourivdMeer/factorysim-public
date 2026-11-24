using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FactorySim.Core.Api;

namespace FactorySim.Core.Tests.Integration;

/// <summary>
/// The test API (plan 21) over real HTTP on 127.0.0.1, with a fake app. A background loop plays
/// the main thread and pumps the queue, except in the time-out test.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestApiServerIntegrationTests : IDisposable
{
    private const string Token = "abcdefghijklmnopqrstuvwxyz234567";

    private readonly FakeSimControl _control = new();
    private readonly MainThreadQueue _queue = new(TimeSpan.FromMilliseconds(300));
    private readonly TestApiServer _server;
    private readonly HttpClient _client = new();
    private readonly CancellationTokenSource _stopPump = new();
    private Thread? _pump;

    public TestApiServerIntegrationTests()
    {
        _server = new TestApiServer(_control, _queue, Token, port: 0);
        _server.Start();
        _client.BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}/");
        _client.Timeout = TimeSpan.FromSeconds(5);
    }

    public void Dispose()
    {
        _stopPump.Cancel();
        _pump?.Join();
        _server.Dispose();
        _client.Dispose();
    }

    private void StartPump()
    {
        _pump = new Thread(() =>
        {
            while (!_stopPump.IsCancellationRequested)
            {
                _queue.Pump();
                Thread.Sleep(2);
            }
        }) { IsBackground = true, Name = "fake main thread" };
        _pump.Start();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json = null, string? token = Token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Add(ApiToken.HeaderName, token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Only_the_right_token_gets_an_answer()
    {
        StartPump();
        HttpResponseMessage none = await Send(HttpMethod.Get, "api/state", token: null);
        HttpResponseMessage wrong = await Send(HttpMethod.Get, "api/state", token: "x" + Token[1..]);
        HttpResponseMessage right = await Send(HttpMethod.Get, "api/state");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("stopped", (await Json(right)).GetProperty("state").GetString());
        Assert.Contains("error", await none.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_a_token_even_an_unknown_path_is_401()
    {
        HttpResponseMessage response = await Send(HttpMethod.Get, "nothing/here", token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void The_server_binds_to_127_0_0_1_only()
    {
        Assert.NotNull(_server.LocalEndpoint);
        Assert.Equal(IPAddress.Loopback, _server.LocalEndpoint!.Address);
        Assert.True(_server.Port > 0);
    }

    [Fact]
    public async Task No_CORS_headers_and_a_browser_origin_is_refused()
    {
        StartPump();
        HttpResponseMessage plain = await Send(HttpMethod.Get, "api/state");
        Assert.DoesNotContain(plain.Headers, header => header.Key.StartsWith("Access-Control", StringComparison.OrdinalIgnoreCase));

        var request = new HttpRequestMessage(HttpMethod.Get, "api/state");
        request.Headers.Add(ApiToken.HeaderName, Token);
        request.Headers.Add("Origin", "https://example.com");
        HttpResponseMessage fromPage = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, fromPage.StatusCode);

        var preflight = new HttpRequestMessage(HttpMethod.Options, "api/state");
        preflight.Headers.Add("Origin", "https://example.com");
        preflight.Headers.Add("Access-Control-Request-Headers", ApiToken.HeaderName);
        HttpResponseMessage answer = await _client.SendAsync(preflight);
        Assert.NotEqual(HttpStatusCode.OK, answer.StatusCode);
        Assert.DoesNotContain(answer.Headers, header => header.Key.StartsWith("Access-Control", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_foreign_host_header_is_refused()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/state");
        request.Headers.Add(ApiToken.HeaderName, Token);
        request.Headers.Host = "evil.example";
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tags_are_listed_with_direction_value_forced_and_address()
    {
        _control.Tags.Store.SetSensor("Pusher back", true);
        _control.Tags.Store.SetSensorNumber("Tank level", 420);

        HttpResponseMessage response = await Send(HttpMethod.Get, "api/tags");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement[] tags = (await Json(response)).GetProperty("tags").EnumerateArray().ToArray();
        Assert.Equal(4, tags.Length);

        JsonElement back = tags.Single(tag => tag.GetProperty("name").GetString() == "Pusher back");
        Assert.Equal("sensor", back.GetProperty("direction").GetString());
        Assert.Equal("bool", back.GetProperty("kind").GetString());
        Assert.True(back.GetProperty("value").GetBoolean());
        Assert.False(back.GetProperty("forced").GetBoolean());
        Assert.Equal("Coil", back.GetProperty("address").GetProperty("area").GetString());
        Assert.Equal(1, back.GetProperty("address").GetProperty("offset").GetInt32());
        Assert.Equal("%IX0.1", back.GetProperty("address").GetProperty("iec").GetString());

        JsonElement tank = tags.Single(tag => tag.GetProperty("name").GetString() == "Tank level");
        Assert.Equal("int", tank.GetProperty("kind").GetString());
        Assert.Equal(420, tank.GetProperty("value").GetInt32());
        Assert.Equal("%", tank.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task One_tag_by_its_encoded_name_and_an_unknown_tag_is_404()
    {
        HttpResponseMessage found = await Send(HttpMethod.Get, "api/tags/Pusher%20back");
        HttpResponseMessage missing = await Send(HttpMethod.Get, "api/tags/Nope");
        HttpResponseMessage slash = await Send(HttpMethod.Get, "api/tags/Pusher%2Fback");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("Pusher back", (await Json(found)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, slash.StatusCode);
    }

    [Fact]
    public async Task Force_and_release_arrive_at_the_store_on_the_main_thread()
    {
        StartPump();
        HttpResponseMessage forced = await Send(HttpMethod.Put, "api/tags/Start%20button/force", "{\"value\": true}");
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.True((await Json(forced)).GetProperty("forced").GetBoolean());
        Assert.Equal(_pump!.ManagedThreadId, _control.ThreadId);
        Assert.True(_control.Tags.Store.Snapshot().Single(tag => tag.Name == "Start button").EffectiveValue);

        HttpResponseMessage number = await Send(HttpMethod.Put, "api/tags/Tank%20level/force", "{\"value\": 5000}");
        Assert.Equal(HttpStatusCode.OK, number.StatusCode);
        Assert.Equal(1000, _control.Tags.Store.Snapshot().Single(tag => tag.Name == "Tank level").EffectiveNumber);

        HttpResponseMessage released = await Send(HttpMethod.Delete, "api/tags/Start%20button/force");
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.False(_control.Tags.Store.Snapshot().Single(tag => tag.Name == "Start button").Forced);
        Assert.Equal(new[] { "force Start button=True", "force Tank level=5000", "release Start button" }, _control.Calls);
    }

    [Fact]
    public async Task Bad_force_requests_are_refused()
    {
        StartPump();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "api/tags/Conveyor/force", "{\"value\": tru")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "api/tags/Conveyor/force", "{\"other\": true}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "api/tags/Conveyor/force", "{\"value\": 3}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "api/tags/Tank%20level/force", "{\"value\": 1.5}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, "api/tags/Nope/force", "{\"value\": true}")).StatusCode);
        Assert.Empty(_control.Calls);

        // 0 and 1 count as a bool, for scripts that think in bits.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "api/tags/Conveyor/force", "{\"value\": 1}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_paths_are_404_and_wrong_methods_405()
    {
        StartPump();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "api/nothing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "api/sim/fly")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "api/level/open", "{\"name\": \"mvp_sorting\"}")).StatusCode);   // plan 33
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(HttpMethod.Get, "api/sim/run")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(HttpMethod.Post, "api/tags")).StatusCode);
    }

    [Fact]
    public async Task Run_pause_and_reset_change_the_state()
    {
        StartPump();
        Assert.Equal("running", (await Json(await Send(HttpMethod.Post, "api/sim/run"))).GetProperty("state").GetString());
        Assert.Equal("paused", (await Json(await Send(HttpMethod.Post, "api/sim/pause"))).GetProperty("state").GetString());
        Assert.Equal("stopped", (await Json(await Send(HttpMethod.Post, "api/sim/reset"))).GetProperty("state").GetString());
        JsonElement state = await Json(await Send(HttpMethod.Get, "api/state"));
        Assert.Equal("Test", state.GetProperty("scene").GetString());
        Assert.False(state.TryGetProperty("level", out _));                // plan 33: the old name is gone
        Assert.Equal(1.0, state.GetProperty("timeScale").GetDouble());
        Assert.Equal(0.0, state.GetProperty("simTime").GetDouble());
    }

    [Fact]
    public async Task Counters_come_from_the_app()
    {
        StartPump();
        JsonElement counters = await Json(await Send(HttpMethod.Get, "api/counters"));
        Assert.Equal(7, counters.GetProperty("spawned").GetInt32());
        Assert.Equal(3, counters.GetProperty("removed").GetProperty("Remover A").GetInt32());
        Assert.Equal(0, counters.GetProperty("fallen").GetInt32());
        Assert.Equal(12.5, counters.GetProperty("leadTimeAverage").GetDouble());
    }

    [Fact]
    public async Task A_scene_opens_by_name_only()
    {
        StartPump();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "api/scene/open", "{\"name\": \"mvp_sorting\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "api/scene/open", "{\"name\": \"elsewhere\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(HttpMethod.Post, "api/scene/open", "{\"name\": \"broken\"}")).StatusCode);
        foreach (string path in new[] { "../secret", "C:\\\\x", "a/b", "..", "user://scenes/x", "" })
        {
            HttpResponseMessage response = await Send(HttpMethod.Post, "api/scene/open", JsonSerializer.Serialize(new { name = path }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "api/scene/open", "not json")).StatusCode);
        Assert.Equal(new[] { "open mvp_sorting", "open elsewhere", "open broken" }, _control.Calls);
        Assert.Equal("mvp_sorting", _control.SceneName);
    }

    [Fact]
    public async Task The_plc_link_is_always_on_127_0_0_1()
    {
        StartPump();
        HttpResponseMessage connected = await Send(HttpMethod.Post, "api/plc/connect", "{\"port\": 5020, \"unit\": 255}");
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.Equal("127.0.0.1", (await Json(connected)).GetProperty("host").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "api/plc/connect", "{\"port\": 502}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "api/plc/connect", "{\"port\": 70000}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "api/plc/connect", "{\"port\": 502, \"unit\": 300}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "api/plc/connect", "{\"port\": 502, \"host\": \"10.0.0.1\"}")).StatusCode);
        Assert.Equal("connecting", (await Json(await Send(HttpMethod.Get, "api/plc"))).GetProperty("status").GetString());
        Assert.Equal("disconnected", (await Json(await Send(HttpMethod.Post, "api/plc/disconnect"))).GetProperty("status").GetString());
        Assert.Equal(new[] { "connect 5020 255", "plc", "connect 502 255", "plc", "plc", "disconnect", "plc" }, _control.Calls);
    }

    [Fact]
    public async Task A_command_the_main_thread_does_not_answer_is_504_and_never_runs_later()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response = await Send(HttpMethod.Post, "api/sim/run");
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.InRange(clock.ElapsedMilliseconds, 250, 3000);

        Assert.Equal(0, _queue.Pump());
        Assert.Empty(_control.Calls);
        Assert.Equal("stopped", ApiState.Name(SimState.Stopped));
    }

    [Fact]
    public async Task Malformed_http_is_400_and_a_big_body_413()
    {
        Assert.Equal("HTTP/1.1 400", (await Raw("GARBAGE\r\n\r\n"))[..12]);
        Assert.Equal("HTTP/1.1 400", (await Raw($"GET /api/state HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n{ApiToken.HeaderName}: {Token}\r\n\r\n"))[..12]);
        Assert.Equal("HTTP/1.1 413", (await Raw($"PUT /api/tags/Conveyor/force HTTP/1.1\r\nHost: 127.0.0.1\r\n{ApiToken.HeaderName}: {Token}\r\nContent-Length: 999999\r\n\r\n"))[..12]);
        Assert.Equal("HTTP/1.1 431", (await Raw("GET / HTTP/1.1\r\nX-Long: " + new string('a', 20000) + "\r\n\r\n"))[..12]);
    }

    [Fact]
    public async Task A_silent_client_does_not_block_the_next_one()
    {
        StartPump();
        using var silent = new TcpClient();
        await silent.ConnectAsync(IPAddress.Loopback, _server.Port);
        HttpResponseMessage response = await Send(HttpMethod.Get, "api/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stopping_frees_the_port_for_a_new_start()
    {
        StartPump();
        int port = _server.Port;
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/state")).StatusCode);
        _server.Stop();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Send(HttpMethod.Get, "api/state"));

        using var again = new TestApiServer(_control, _queue, Token, port);
        again.Start();
        Assert.Equal(port, again.Port);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "api/state")).StatusCode);
    }

    [Fact]
    public void A_busy_port_throws_on_start()
    {
        using var second = new TestApiServer(_control, _queue, Token, _server.Port);
        Assert.ThrowsAny<SocketException>(second.Start);
    }

    /// <summary>Sends bytes as they are and returns the start of the answer.</summary>
    private async Task<string> Raw(string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.Port);
        NetworkStream stream = client.GetStream();
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        try
        {
            await stream.WriteAsync(bytes);
        }
        catch (IOException)
        {
            // The server may close before it has read everything; the answer is still there.
        }

        var buffer = new byte[256];
        int read = await stream.ReadAsync(buffer);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
