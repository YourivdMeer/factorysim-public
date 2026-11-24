using FactorySim.Core.Plc;
using FactorySim.Core.Plc.TestPlc;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 29: the built-in test PLC. It runs the sorter of the MVP scene on a free port on 127.0.0.1, so the app
/// works without Python or CODESYS. It stops with Verbreken, another connection, another scene and on exit.
/// </summary>
public partial class Main
{
    private TestPlcServer? _testPlc;

    /// <summary>The built-in test PLC while it runs, else null.</summary>
    public TestPlcServer? TestPlc => _testPlc;

    /// <summary>
    /// Starts the test PLC (sorter) and connects to it; replaces any existing link. Does nothing when the scene
    /// does not fit the sorter (plan 34), also when the disabled button is pressed from code.
    /// </summary>
    public void StartTestPlc()
    {
        TestPlcFit fit = TestPlcFit.Check(_tagMap.Entries);
        if (!fit.Fits)
        {
            GD.Print($"Test-PLC not started: {fit.Reason}");
            return;
        }

        DisconnectPlc();
        _testPlc = TestPlcServer.Start(TestPlcMode.Sorter);
        ConnectLink(new ModbusLinkOptions { Host = TestPlcServer.LoopbackHost, Port = _testPlc.Port, UnitId = 1 });
    }

    private void StopTestPlc()
    {
        _testPlc?.Dispose();
        _testPlc = null;
    }
}
