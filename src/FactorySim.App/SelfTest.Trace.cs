using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "trace" (plan 10): sort 10 boxes with the program of sort:20 while the tab Trace is open,
/// then check the recording: one rising edge of Pusher per high box, each after a rising edge of
/// At pusher within 1 s, the same edges in the CSV export in rising time; 30 s with and 30 s without
/// recording for the frame rate; and an empty trace after Reset.
/// </summary>
public partial class SelfTest
{
    private const int TraceBoxes = 10;
    private const double TraceFpsSeconds = 30.0;

    private int _trPhase;
    private bool _trPass = true;
    private bool _trSortDone;
    private double _trSortDoneAt = -1.0;
    private double _trPhaseStart;
    private int _trFrames;
    private readonly Stopwatch _trClock = new();
    private double _trFpsWithout;
    private int _trResetFrames;

    private TagRecorder Recorder => Main.Recorder;

    private void StartTrace()
    {
        Main.Hud.ShowTrace();
        Main.Sim.Run();
    }

    /// <summary>Physics side: sorting in phase 0, the line running without pushing in the frame-rate phases.</summary>
    private void RunTracePhysics()
    {
        (_, int fallen) = Main.CountBoxes();
        _maxFallen = Math.Max(_maxFallen, fallen);
        if (_trPhase == 0)
        {
            if (RunSortFor(TraceBoxes) && _trSortDoneAt < 0.0)
            {
                _trSortDoneAt = _elapsed;
            }

            _trSortDone = _trSortDoneAt >= 0.0 && _elapsed - _trSortDoneAt > SettleSeconds;
            return;
        }

        WriteActuators(conveyor: true, pusher: false, emitter: true);
    }

    /// <summary>Frame side: the checks, the frame-rate phases and the reset.</summary>
    private void RunTraceFrame()
    {
        switch (_trPhase)
        {
            case 0 when _trSortDone:
                CheckTraceRecording();
                SaveScreenshot(suffix: "_trace");
                Recorder.Paused = true;
                StartTraceFpsPhase(1);
                break;
            case 1 or 2:
                _trFrames++;
                if (Main.Sim.SimTime - _trPhaseStart < TraceFpsSeconds)
                {
                    break;
                }

                double fps = _trFrames / _trClock.Elapsed.TotalSeconds;
                if (_trPhase == 1)
                {
                    _trFpsWithout = fps;
                    Recorder.Paused = false;
                    StartTraceFpsPhase(2);
                    break;
                }

                JudgeTraceFps(_trFpsWithout, fps);
                Main.Sim.Reset();
                _trPhase = 3;
                break;
            case 3:
                if (++_trResetFrames < 10)
                {
                    break;
                }

                bool empty = Recorder.Count == 0 && Recorder.Tags.Count == 0;
                GD.Print($"SELFTEST trace after reset: changes={Recorder.Count} tags={Recorder.Tags.Count} -> {(empty ? "ok" : "FAILED")}");
                _trPass &= empty;
                _reported = true;
                GD.Print(_trPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_trPass ? 0 : 1);
                break;
        }
    }

    private void StartTraceFpsPhase(int phase)
    {
        _trPhase = phase;
        _trPhaseStart = Main.Sim.SimTime;
        _trFrames = 0;
        _trClock.Restart();
    }

    private void CheckTraceRecording()
    {
        int removed = EndRemover.Count + LaneRemover.Count;
        bool sorted = _maxFallen == 0 && removed == TraceBoxes && EndRemover.HighCount == 0 && LaneRemover.LowCount == 0;
        TraceCheck($"sorted removed={removed}/{TraceBoxes} high={LaneRemover.Count} fallen={_maxFallen}", sorted);

        IReadOnlyList<double> pushes = Recorder.Edges(TagNames.Pusher, rising: true);
        IReadOnlyList<double> arrivals = Recorder.Edges(TagNames.AtPusher, rising: true);
        TraceCheck($"rising edges of Pusher = high boxes ({pushes.Count} vs {LaneRemover.Count})",
            pushes.Count == LaneRemover.Count && pushes.Count > 0);

        var delays = new List<double>();
        foreach (double push in pushes)
        {
            double arrival = arrivals.Where(t => t <= push).DefaultIfEmpty(double.NaN).Max();
            delays.Add(push - arrival);
        }

        bool delaysOk = delays.All(d => d is >= 0.0 and <= 1.0);
        TraceCheck("At pusher rises 0–1 s before each push: "
            + string.Join(", ", delays.Select(d => (d * 1000.0).ToString("0", CultureInfo.InvariantCulture) + " ms")), delaysOk);

        string? path = Main.Hud.Trace.Export();
        if (path is null)
        {
            TraceCheck("CSV export written", false);
            return;
        }

        string[] lines = File.ReadAllLines(path);
        var times = new List<double>();
        var csvPushes = new List<string>();
        int previous = -1;
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(';');
            times.Add(double.Parse(cells[0], CultureInfo.InvariantCulture));
            if (cells[1] == TagNames.Pusher)
            {
                int value = int.Parse(cells[2], CultureInfo.InvariantCulture);
                if (previous == 0 && value == 1)
                {
                    csvPushes.Add(cells[0]);
                }

                previous = value;
            }
        }

        bool ascending = times.Zip(times.Skip(1), (a, b) => a <= b).All(ok => ok);
        bool same = csvPushes.SequenceEqual(pushes.Select(t => t.ToString("0.000", CultureInfo.InvariantCulture)));
        TraceCheck($"CSV {path}: header={lines.FirstOrDefault()} rows={lines.Length - 1} pusher edges={csvPushes.Count} ascending={ascending}",
            lines.FirstOrDefault() == "Time;Tag;Value;Forced" && ascending && same);
        GD.Print($"SELFTEST trace recording: tags={Recorder.Tags.Count} changes={Recorder.Count} "
            + $"origin={Recorder.Origin:0.000} end={Recorder.End:0.000} lanes={Main.Hud.Trace.Lanes.Count}");
    }

    private void JudgeTraceFps(double without, double with)
    {
        double difference = without > 0.0 ? (without - with) / without : 0.0;
        bool windowed = DisplayServer.GetName() != "headless";
        bool judged = windowed && without >= MinFpsForTiming;
        bool ok = !judged || difference < 0.05;
        GD.Print($"SELFTEST trace fps: without recording {without:0.0}, with recording {with:0.0}, "
            + $"difference {difference * 100.0:0.0}% (window={windowed}) -> {(ok ? "ok" : "FAILED")}");
        if (!judged)
        {
            GD.Print($"SELFTEST trace fps niet beoordeeld: {(windowed ? $"fps {without:0} < {MinFpsForTiming}" : "headless, alleen gelogd")}");
        }

        _trPass &= ok;
    }

    private void TraceCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST trace {what}: {(ok ? "ok" : "FAILED")}");
        _trPass &= ok;
    }
}
