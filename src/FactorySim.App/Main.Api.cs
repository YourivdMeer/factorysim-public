using System;
using System.Linq;
using System.Net.Sockets;
using FactorySim.Core;
using FactorySim.Core.Api;
using FactorySim.Core.Scenes;
using FactorySim.Core.Plc;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 21: the local test API. Main is the <see cref="ISimControl"/> behind it; the server thread hands
/// its work over through <see cref="_apiQueue"/>, which <see cref="_Process"/> pumps every frame.
/// </summary>
public partial class Main : ISimControl
{
    public const string ApiTokenPath = "user://api_token.txt";

    private readonly MainThreadQueue _apiQueue = new(TimeSpan.FromSeconds(5));
    private TestApiServer? _api;
    private volatile TagSource _tagSource = null!;
    private ModbusLinkOptions? _linkOptions;

    /// <summary>The running test API, or null when it is off.</summary>
    public TestApiServer? Api => _api;

    /// <summary>The token of the running test API, or null.</summary>
    public string? ApiTokenText { get; private set; }

    /// <summary>Starts the test API on 127.0.0.1 (0: any free port). Returns an error text, or null.</summary>
    public string? StartApi(int port)
    {
        StopApi();
        string token = ApiToken.New();
        var server = new TestApiServer(this, _apiQueue, token, port);
        try
        {
            server.Start();
        }
        catch (SocketException e)
        {
            string error = $"Test-API: poort {port} is niet beschikbaar ({e.SocketErrorCode}).";
            GD.PushWarning(error);
            return error;
        }

        _api = server;
        ApiTokenText = token;
        if (!SelfTestRun)
        {
            try
            {
                System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(ApiTokenPath), token);
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Test-API: token niet weggeschreven: {e.Message}");
            }
        }

        GD.Print($"Test-API luistert op 127.0.0.1:{server.Port}");
        return null;
    }

    public void StopApi()
    {
        if (_api is null)
        {
            return;
        }

        _api.Dispose();
        _api = null;
        ApiTokenText = null;
        if (!SelfTestRun)
        {
            try
            {
                System.IO.File.Delete(ProjectSettings.GlobalizePath(ApiTokenPath));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                // The next start writes a new one.
            }
        }
    }

    /// <summary>--api-port=5080 switches the API on at start.</summary>
    private void StartApiIfRequested()
    {
        if (UserArgument("--api-port=") is { } text)
        {
            if (int.TryParse(text, out int port) && port is >= 0 and <= 65535)
            {
                StartApi(port);
            }
            else
            {
                GD.PushWarning($"Test-API: ongeldige poort '{text}'.");
            }
        }
    }

    // ------------------------------------------------------------ ISimControl

    TagSource ISimControl.Tags => _tagSource;

    ApiState ISimControl.State() => new(ApiState.Name(_sim.State), _scene.Name, _sim.SimTime, _sim.TimeScale);

    void ISimControl.Run() => _sim.Run();

    void ISimControl.Pause() => _sim.Pause();

    void ISimControl.Reset() => _sim.Reset();

    void ISimControl.Force(string tag, bool value) => _hud.SetForce(tag, value ? 2 : 1);

    void ISimControl.Force(string tag, int number) => _hud.SetForceNumber(tag, number);

    void ISimControl.Release(string tag)
    {
        if (_tags.Find(tag)?.Kind == TagKind.Int)
        {
            _hud.SetForceNumber(tag, null);
        }
        else
        {
            _hud.SetForce(tag, 0);
        }
    }

    ApiCounters ISimControl.Counters()
    {
        (int inScene, int fallen) = CountBoxes();
        return new ApiCounters(
            _view.Emitters.Sum(e => e.EmittedCount),
            _view.Removers.ToDictionary(r => r.Name, r => r.Remover.Count),
            inScene,
            fallen,
            _stats.PerMinute,
            _stats.PerMinuteWindow,
            _stats.Total.LeadTimeAverage);
    }

    /// <summary>The examples first, then the own scenes folder; through the editor, as the gallery opens a scene.</summary>
    string? ISimControl.OpenScene(string name)
    {
        string file = name + SceneEditor.FileExtension;
        string path = new[] { ScenePaths.ExamplesFolder + "/" + file, ScenePaths.OwnFolder + "/" + file }.FirstOrDefault(FileAccess.FileExists)
            ?? throw new System.Collections.Generic.KeyNotFoundException(name);
        return _editor.LoadFrom(path);
    }

    void ISimControl.ConnectPlc(int port, byte unitId) =>
        ConnectPlc(new ModbusLinkOptions { Host = ApiRouter.PlcHost, Port = port, UnitId = unitId });

    void ISimControl.DisconnectPlc() => DisconnectPlc();

    ApiPlcStatus ISimControl.Plc() =>
        _link is null || _linkOptions is null
            ? ApiPlcStatus.Disconnected
            : ApiPlcStatus.Of(_link.Status, _link.Statistics, _linkOptions.Host, _linkOptions.Port);
}
