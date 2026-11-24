using System.Text.Json;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Api;

/// <summary>
/// The endpoints of the test API (plan 21). Runs on the server thread: tag reads go straight to the
/// thread-safe store, everything else to the main thread through the <see cref="MainThreadQueue"/>.
/// </summary>
public sealed class ApiRouter
{
    public const string PlcHost = "127.0.0.1";

    private readonly ISimControl _control;
    private readonly MainThreadQueue _queue;

    public ApiRouter(ISimControl control, MainThreadQueue queue)
    {
        _control = control;
        _queue = queue;
    }

    /// <summary>Answers an authorised request.</summary>
    public ApiResponse Handle(ApiRequest request)
    {
        try
        {
            return Route(request);
        }
        catch (TimeoutException)
        {
            return ApiResponse.Error(504, "The app did not answer in time (main thread busy?).");
        }
        catch (JsonException)
        {
            return ApiResponse.Error(400, "The body is not valid JSON.");
        }
    }

    private ApiResponse Route(ApiRequest request)
    {
        IReadOnlyList<string> s = request.Segments;
        string method = request.Method;
        if (s.Count < 2 || s[0] != "api")
        {
            return NotFound();
        }

        switch (s[1])
        {
            case "state" when s.Count == 2:
                return Only(method, "GET") ?? ApiResponse.Ok(OnMain(_control.State));

            case "sim" when s.Count == 3 && s[2] is "run" or "pause" or "reset":
                return Only(method, "POST") ?? SimCommand(s[2]);

            case "tags" when s.Count == 2:
                return Only(method, "GET") ?? AllTags(_control.Tags);

            case "tags" when s.Count == 3:
                return Only(method, "GET") ?? TagAnswer(_control.Tags, s[2]);

            case "tags" when s.Count == 4 && s[3] == "force":
                return method switch
                {
                    "PUT" => Force(s[2], request.Body),
                    "DELETE" => Release(s[2]),
                    _ => ApiResponse.Error(405, "Use PUT or DELETE."),
                };

            case "counters" when s.Count == 2:
                return Only(method, "GET") ?? ApiResponse.Ok(OnMain(_control.Counters));

            case "scene" when s.Count == 3 && s[2] == "open":
                return Only(method, "POST") ?? OpenScene(request.Body);

            case "plc" when s.Count == 2:
                return Only(method, "GET") ?? ApiResponse.Ok(OnMain(_control.Plc));

            case "plc" when s.Count == 3 && s[2] == "connect":
                return Only(method, "POST") ?? Connect(request.Body);

            case "plc" when s.Count == 3 && s[2] == "disconnect":
                return Only(method, "POST") ?? ApiResponse.Ok(OnMain(() =>
                {
                    _control.DisconnectPlc();
                    return _control.Plc();
                }));

            default:
                return NotFound();
        }
    }

    private ApiResponse SimCommand(string command) => ApiResponse.Ok(OnMain(() =>
    {
        switch (command)
        {
            case "run":
                _control.Run();
                break;
            case "pause":
                _control.Pause();
                break;
            default:
                _control.Reset();
                break;
        }

        return _control.State();
    }));

    private ApiResponse Force(string tag, byte[] body)
    {
        TagSource source = _control.Tags;
        TagDefinition? definition = source.Store.Find(tag);
        if (definition is null)
        {
            return UnknownTag(tag);
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("value", out JsonElement value))
        {
            return ApiResponse.Error(400, "Send {\"value\": …}.");
        }

        if (definition.Kind == TagKind.Int)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            {
                return ApiResponse.Error(400, $"'{tag}' is an Int tag: send a whole number.");
            }

            OnMain(() => Done(() => _control.Force(tag, number)));
        }
        else
        {
            bool? on = value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt32(out int bit) && bit is 0 or 1 => bit == 1,
                _ => null,
            };
            if (on is null)
            {
                return ApiResponse.Error(400, $"'{tag}' is a bool tag: send true, false, 0 or 1.");
            }

            OnMain(() => Done(() => _control.Force(tag, on.Value)));
        }

        return TagAnswer(source, tag);
    }

    private ApiResponse Release(string tag)
    {
        TagSource source = _control.Tags;
        if (source.Store.Find(tag) is null)
        {
            return UnknownTag(tag);
        }

        OnMain(() => Done(() => _control.Release(tag)));
        return TagAnswer(source, tag);
    }

    private ApiResponse OpenScene(byte[] body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        string? name = document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("name", out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        if (!SceneName.IsValid(name))
        {
            return ApiResponse.Error(400, "Send {\"name\": \"…\"}: a scene name from the examples or the own scenes folder, not a path.");
        }

        string? error;
        try
        {
            error = OnMain(() => _control.OpenScene(name!));
        }
        catch (KeyNotFoundException)
        {
            return ApiResponse.Error(404, $"No scene '{name}' in the examples or the own scenes folder.");
        }

        return error is null ? ApiResponse.Ok(new { opened = name }) : ApiResponse.Error(422, error);
    }

    private ApiResponse Connect(byte[] body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ApiResponse.Error(400, "Send {\"port\": 5020, \"unit\": 255}.");
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name is not ("port" or "unit"))
            {
                return ApiResponse.Error(400, $"Unknown field '{property.Name}'. The host is always {PlcHost}.");
            }
        }

        if (!root.TryGetProperty("port", out JsonElement portElement) || portElement.ValueKind != JsonValueKind.Number
            || !portElement.TryGetInt32(out int port) || port is < 1 or > 65535)
        {
            return ApiResponse.Error(400, "'port' must be a whole number from 1 to 65535.");
        }

        int unit = 255;
        if (root.TryGetProperty("unit", out JsonElement unitElement)
            && (unitElement.ValueKind != JsonValueKind.Number || !unitElement.TryGetInt32(out unit) || unit is < 0 or > 255))
        {
            return ApiResponse.Error(400, "'unit' must be a whole number from 0 to 255.");
        }

        return ApiResponse.Ok(OnMain(() =>
        {
            _control.ConnectPlc(port, (byte)unit);
            return _control.Plc();
        }));
    }

    /// <summary>Every tag of one store; the caller reads <see cref="ISimControl.Tags"/> once, so a scene load cannot split the answer.</summary>
    private static ApiResponse AllTags(TagSource source)
    {
        IReadOnlyList<TagState> states = source.Store.Snapshot();
        return ApiResponse.Ok(new { tags = source.Map.Entries.Select(e => states.FirstOrDefault(t => t.Name == e.Name)).Where(t => t is not null).Select(t => TagJson(source, t!)).ToArray() });
    }

    private static ApiResponse TagAnswer(TagSource source, string name)
    {
        TagState? state = source.Store.Snapshot().FirstOrDefault(tag => tag.Name == name);
        return state is null ? UnknownTag(name) : ApiResponse.Ok(TagJson(source, state));
    }

    /// <summary>One tag as JSON, read straight from the store (thread-safe).</summary>
    private static object TagJson(TagSource source, TagState state)
    {
        string name = state.Name;
        TagMapEntry? entry = source.Map.Entries.FirstOrDefault(e => e.Name == name);
        bool analog = state.Kind == TagKind.Int;
        return new
        {
            name,
            direction = state.Direction == TagDirection.Sensor ? "sensor" : "actuator",
            kind = analog ? "int" : "bool",
            value = analog ? (object)state.EffectiveNumber : state.EffectiveValue,
            forced = state.Forced,
            unit = analog ? state.Unit : null,
            scale = analog ? state.Scale : (double?)null,
            address = entry is null ? null : new { area = entry.Area.ToString(), offset = entry.Offset, iec = entry.IecAddress },
        };
    }

    private T OnMain<T>(Func<T> work) =>
        _queue.TryInvoke(work, out T result) ? result : throw new TimeoutException();

    private static bool Done(Action action)
    {
        action();
        return true;
    }

    private static ApiResponse? Only(string method, string allowed) =>
        method == allowed ? null : ApiResponse.Error(405, $"Use {allowed}.");

    private static ApiResponse NotFound() => ApiResponse.Error(404, "Unknown path. See docs/SCENES.md, section Test-API.");

    private static ApiResponse UnknownTag(string tag) => ApiResponse.Error(404, $"No tag '{tag}' in this scene.");
}
