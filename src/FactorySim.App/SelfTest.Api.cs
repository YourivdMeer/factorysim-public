using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FactorySim.Core;
using FactorySim.Core.Api;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "api" (plan 21): switches the test API on at a free port and runs tools/api/run_test.py as a
/// subprocess with the token and the port of the fake PLC (--plc-port=, started by regress.sh). The
/// script opens mvp_sorting, connects, runs, presses Start and judges the counters. The process is
/// polled from _Process, never waited for: the main thread must keep pumping the API queue.
/// Afterwards the counters, the PLC window and the freed port are checked.
/// </summary>
public partial class SelfTest
{
    private const double ApiScriptSeconds = 40.0;
    private const double ApiTimeoutSeconds = 150.0;

    private int _apiPhase;
    private bool _apiPass = true;
    private int _apiPort;
    private double _apiStartedAt;
    private Process? _apiProcess;
    private readonly ConcurrentQueue<string> _apiOutput = new();

    private void ApiCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST api {what}: {(ok ? "ok" : "FAILED")}");
        _apiPass &= ok;
    }

    private void RunApiFrame()
    {
        while (_apiOutput.TryDequeue(out string? line))
        {
            GD.Print($"SELFTEST api script: {line}");
        }

        switch (_apiPhase)
        {
            case 0:
            {
                string? error = Main.StartApi(0);
                _apiPort = Main.Api?.Port ?? 0;
                ApiCheck($"started on 127.0.0.1:{_apiPort} ({error ?? "no error"})", error is null && _apiPort > 0
                    && Main.Api?.LocalEndpoint?.Address.Equals(IPAddress.Loopback) == true);
                ApiCheck("token of 32 characters", Main.ApiTokenText?.Length == ApiToken.Length);
                if (error is not null)
                {
                    FinishApi();
                    return;
                }

                _apiPhase = 1;
                return;
            }

            case 1:
            {
                // One frame later the PLC window follows the server.
                ApiCheck($"PLC window: '{Main.Hud.ApiStatusText}'", Main.Hud.ApiToggle.ButtonPressed
                    && Main.Hud.ApiStatusText == $"Luistert op 127.0.0.1:{_apiPort}");
                string script = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    ProjectSettings.GlobalizePath("res://"), "..", "..", "tools", "api", "run_test.py"));
                var start = new ProcessStartInfo("py")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                foreach (string argument in new[]
                {
                    script, "--api-port", _apiPort.ToString(), "--token", Main.ApiTokenText!,
                    "--plc-port", PlcPort.ToString(), "--seconds", ApiScriptSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })
                {
                    start.ArgumentList.Add(argument);
                }

                try
                {
                    _apiProcess = Process.Start(start);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    ApiCheck($"start py {script}: {e.Message}", false);
                    FinishApi();
                    return;
                }

                if (_apiProcess is null)
                {
                    ApiCheck("start py", false);
                    FinishApi();
                    return;
                }

                _apiProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) { _apiOutput.Enqueue(e.Data); } };
                _apiProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) { _apiOutput.Enqueue("stderr: " + e.Data); } };
                _apiProcess.BeginOutputReadLine();
                _apiProcess.BeginErrorReadLine();
                _apiStartedAt = WallSeconds();
                _apiPhase = 2;
                return;
            }

            case 2:
            {
                if (!_apiProcess!.HasExited)
                {
                    if (WallSeconds() - _apiStartedAt > ApiTimeoutSeconds)
                    {
                        _apiProcess.Kill();
                        ApiCheck($"script still running after {ApiTimeoutSeconds:0} s", false);
                        FinishApi();
                    }

                    return;
                }

                _apiProcess.WaitForExit();                  // flushes the redirected output; the process has ended
                while (_apiOutput.TryDequeue(out string? line))
                {
                    GD.Print($"SELFTEST api script: {line}");
                }

                ApiCheck($"script exit code {_apiProcess.ExitCode} after {WallSeconds() - _apiStartedAt:0.0} s", _apiProcess.ExitCode == 0);
                int end = EndRemover.Count;
                int lane = LaneRemover.Count;
                ApiCheck($"scene {Main.CurrentScenePath}: end {end}, lane {lane}, emitted {Emitter.EmittedCount}, fallen {Main.Stats.FallenCount}",
                    (Main.CurrentScenePath ?? string.Empty).EndsWith("/mvp_sorting.fsscene.json", StringComparison.Ordinal) && end > 0 && lane > 0 && EndRemover.HighCount == 0 && LaneRemover.LowCount == 0);
                ApiCheck($"after the script: sim {Main.Sim.State}, link {(Main.Link is null ? "none" : Main.Link.Status.ToString())}",
                    Main.Sim.State == SimState.Paused && Main.Link is null);
                FinishApi();
                return;
            }

            case 4:
                ApiCheck($"PLC window after stop: '{Main.Hud.ApiStatusText}'", !Main.Hud.ApiToggle.ButtonPressed && Main.Hud.ApiStatusText == "Uit");
                GD.Print(_apiPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                _reported = true;
                GetTree().Quit(_apiPass ? 0 : 1);
                return;
        }
    }

    /// <summary>Stops the API and checks that the port is free again; the next frame reports.</summary>
    private void FinishApi()
    {
        if (_apiProcess is { HasExited: false })
        {
            _apiProcess.Kill();
        }

        Main.StopApi();
        bool free;
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, _apiPort);
            listener.Start();
            listener.Stop();
            free = _apiPort > 0;
        }
        catch (SocketException)
        {
            free = false;
        }

        ApiCheck($"port {_apiPort} free after stop", free && Main.Api is null && Main.ApiTokenText is null);
        _apiPhase = 4;
    }
}
