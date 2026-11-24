using FactorySim.Core.Api;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests.Integration;

/// <summary>The app as the test API sees it, without Godot: a real tag store and a record of the calls.</summary>
public sealed class FakeSimControl : ISimControl
{
    private readonly SimController _sim = new();

    public FakeSimControl()
    {
        var map = new TagMap(new[]
        {
            new TagMapEntry("Start button", TagDirection.Sensor, ModbusArea.Coil, 0, "%IX0.0"),
            new TagMapEntry("Pusher back", TagDirection.Sensor, ModbusArea.Coil, 1, "%IX0.1"),
            new TagMapEntry("Conveyor", TagDirection.Actuator, ModbusArea.DiscreteInput, 0, "%QX0.0"),
            new TagMapEntry("Tank level", TagDirection.Sensor, ModbusArea.HoldingRegister, 0, "%IW0", Kind: TagKind.Int, Scale: 10, Unit: "%", Min: 0, Max: 1000),
        });
        Tags = new TagSource(new TagStore(map.ToDefinitions()), map);
    }

    public TagSource Tags { get; }

    public int ThreadId { get; private set; }

    public List<string> Calls { get; } = new();

    public string SceneName { get; private set; } = "Test";

    public ApiPlcStatus PlcStatus { get; set; } = ApiPlcStatus.Disconnected;

    public ApiState State()
    {
        Note("state");
        return new ApiState(ApiState.Name(_sim.State), SceneName, _sim.SimTime, _sim.TimeScale);
    }

    public void Run()
    {
        Note("run");
        _sim.Run();
    }

    public void Pause()
    {
        Note("pause");
        _sim.Pause();
    }

    public void Reset()
    {
        Note("reset");
        _sim.Reset();
    }

    public void Force(string tag, bool value)
    {
        Note($"force {tag}={value}");
        Tags.Store.Force(tag, value);
    }

    public void Force(string tag, int number)
    {
        Note($"force {tag}={number}");
        Tags.Store.Force(tag, number);
    }

    public void Release(string tag)
    {
        Note($"release {tag}");
        Tags.Store.Release(tag);
    }

    public ApiCounters Counters()
    {
        Note("counters");
        return new ApiCounters(7, new Dictionary<string, int> { ["Remover A"] = 3, ["Remover B"] = 2 }, 2, 0, 5.0, 4.0, 12.5);
    }

    public string? OpenScene(string name)
    {
        Note($"open {name}");
        if (name == "broken")
        {
            return "het bestand is geen scène";
        }

        if (name != "mvp_sorting")
        {
            throw new KeyNotFoundException(name);
        }

        SceneName = name;
        return null;
    }

    public void ConnectPlc(int port, byte unitId)
    {
        Note($"connect {port} {unitId}");
        PlcStatus = new ApiPlcStatus("connecting", "127.0.0.1", port, 0, 0, 0, 0, 0, null);
    }

    public void DisconnectPlc()
    {
        Note("disconnect");
        PlcStatus = ApiPlcStatus.Disconnected;
    }

    public ApiPlcStatus Plc()
    {
        Note("plc");
        return PlcStatus;
    }

    private void Note(string call)
    {
        ThreadId = Environment.CurrentManagedThreadId;
        lock (Calls)
        {
            Calls.Add(call);
        }
    }
}
