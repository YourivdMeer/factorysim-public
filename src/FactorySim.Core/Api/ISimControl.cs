namespace FactorySim.Core.Api;

/// <summary>
/// What the test API may do with the app (plan 21). <see cref="Tags"/> is read on the server thread;
/// every other member is called on the main thread only, through <see cref="MainThreadQueue"/>.
/// </summary>
public interface ISimControl
{
    /// <summary>The current tag store with its map. Thread-safe: the app swaps the pair as one reference.</summary>
    TagSource Tags { get; }

    ApiState State();

    void Run();

    void Pause();

    void Reset();

    /// <summary>Forces a bool tag, as the force column of the tag table does.</summary>
    void Force(string tag, bool value);

    /// <summary>Forces an Int tag to a number, clipped to its range.</summary>
    void Force(string tag, int number);

    void Release(string tag);

    ApiCounters Counters();

    /// <summary>
    /// Opens a scene by name from the examples or the own scenes folder. The name is already checked
    /// with <see cref="SceneName.IsValid"/>. Returns an error text, or null.
    /// Throws <see cref="KeyNotFoundException"/> when there is no scene with that name.
    /// </summary>
    string? OpenScene(string name);

    /// <summary>Connects to a Modbus server on 127.0.0.1; the API cannot choose another host.</summary>
    void ConnectPlc(int port, byte unitId);

    void DisconnectPlc();

    ApiPlcStatus Plc();
}
