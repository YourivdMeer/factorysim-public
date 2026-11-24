using System;
using System.Diagnostics;
using FactorySim.Core;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "timescale" (plan 09): the sorting line of sort:20 at ×4 and at ×0,25, ten single
/// steps while paused, and a reset. Real time comes from a Stopwatch, never from _elapsed: the
/// delta in _Process scales with Engine.TimeScale.
/// </summary>
public partial class SelfTest
{
    private const double TickSeconds = 1.0 / 120.0;

    /// <summary>
    /// Below this average frame rate the engine cannot deliver the ticks of ×4 (8–32 steps per frame),
    /// so real time says something about the machine, not about the time scale. Seen with a window on
    /// an unattended pc: 1–2 fps even at ×1.
    /// </summary>
    private const double MinFpsForTiming = 60.0;

    private int _tsFrames;

    private readonly Stopwatch _tsClock = new();
    private TickCounter _tsCounter = null!;
    private int _tsPhase;
    private bool _tsPhaseDone;
    private bool _tsPass = true;
    private int _tsTarget;
    private double _tsStepStart;
    private long _tsTicksStart;
    private int _tsStepsIssued;
    private int _tsWaitFrames;

    private void StartTimeScale()
    {
        _tsCounter = new TickCounter { Name = "TickCounter" };
        Main.World.AddChild(_tsCounter);
        StartSortPhase(4.0, 20);
    }

    private void StartSortPhase(double scale, int target)
    {
        Main.Sim.Reset();
        Main.Sim.SetTimeScale(scale);
        _tsTarget = target;
        _step = Step.Transport;
        _elapsed = 0.0;
        _maxFallen = 0;
        _tsPhaseDone = false;
        _tsFrames = 0;
        Main.Sim.Run();
        _tsClock.Restart();
    }

    /// <summary>Physics side: the sorting PLC on tags, as in sort:N.</summary>
    private void RunTimeScalePhysics()
    {
        if (_tsPhaseDone || _tsPhase > 1)
        {
            return;
        }

        (_, int fallen) = Main.CountBoxes();
        _maxFallen = Math.Max(_maxFallen, fallen);
        RunSortFor(_tsTarget);
        int removed = EndRemover.Count + LaneRemover.Count;
        if (removed >= _tsTarget || _elapsed > _tsTarget * 6.0 + 30.0)
        {
            _tsPhaseDone = true;
        }
    }

    /// <summary>Frame side: judges each phase on real time and moves on.</summary>
    private void RunTimeScaleFrame()
    {
        _tsFrames++;
        switch (_tsPhase)
        {
            case 0 or 1 when _tsPhaseDone:
                JudgeSortPhase();
                if (_tsPhase == 0)
                {
                    _tsPhase = 1;
                    StartSortPhase(0.25, 4);
                }
                else
                {
                    _tsPhase = 2;
                    Main.Sim.Pause();
                    _tsStepStart = Main.Sim.SimTime;
                    _tsTicksStart = _tsCounter.Ticks;
                    _tsStepsIssued = 0;
                    _tsWaitFrames = 0;
                }

                break;
            case 2:
                RunSteps();
                break;
            case 3:
                Main.Sim.Reset();
                bool resetOk = Main.Sim.TimeScale == 1.0 && Main.Sim.SimTime == 0.0
                    && Engine.PhysicsTicksPerSecond == 120 && Engine.TimeScale == 1.0;
                GD.Print($"SELFTEST timescale reset: scale={Main.Sim.TimeScale} simTime={Main.Sim.SimTime} "
                    + $"tps={Engine.PhysicsTicksPerSecond} engineScale={Engine.TimeScale} -> {(resetOk ? "ok" : "FAILED")}");
                _tsPass &= resetOk;
                _reported = true;
                GD.Print(_tsPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_tsPass ? 0 : 1);
                break;
        }
    }

    private void JudgeSortPhase()
    {
        double real = _tsClock.Elapsed.TotalSeconds;
        double sim = Main.Sim.SimTime;
        double scale = Main.Sim.TimeScale;
        double ratio = real / sim;
        (double low, double high) = scale > 1.0 ? (0.15, 0.5) : (2.5, 6.0);
        int removed = EndRemover.Count + LaneRemover.Count;
        bool sorted = _maxFallen == 0 && removed == _tsTarget
            && EndRemover.HighCount == 0 && LaneRemover.LowCount == 0;
        double fps = _tsFrames / real;
        bool judged = fps >= MinFpsForTiming;
        bool timing = !judged || ratio >= low && ratio <= high;
        GD.Print($"SELFTEST timescale {SimController.ScaleLabel(scale)}: removed={removed}/{_tsTarget} "
            + $"end low={EndRemover.LowCount} high={EndRemover.HighCount} lane low={LaneRemover.LowCount} high={LaneRemover.HighCount} "
            + $"fallen={_maxFallen} sim={sim:0.00}s real={real:0.00}s real/sim={ratio:0.000} (allowed {low}-{high}) "
            + $"fps={fps:0} -> {(sorted && timing ? "ok" : "FAILED")}");
        if (!judged)
        {
            GD.Print($"SELFTEST timescale {SimController.ScaleLabel(scale)}: timing niet beoordeeld: fps {fps:0} < {MinFpsForTiming}");
        }

        _tsPass &= sorted && timing;
    }

    /// <summary>Ten single steps, each only after the previous one ran; then a few idle frames.</summary>
    private void RunSteps()
    {
        double done = Main.Sim.SimTime - _tsStepStart;
        bool previousRan = Math.Abs(done - _tsStepsIssued * TickSeconds) < 1e-9;
        if (_tsStepsIssued < 10)
        {
            if (previousRan)
            {
                Main.Sim.Step();
                _tsStepsIssued++;
            }

            return;
        }

        // Wait a few frames after the last step: nothing more may run.
        if (++_tsWaitFrames < 30)
        {
            return;
        }

        long ticks = _tsCounter.Ticks - _tsTicksStart;
        bool ok = Math.Abs(done - 10 * TickSeconds) < 1e-9 && ticks == 10
            && Main.Sim.State == SimState.Paused;
        GD.Print($"SELFTEST timescale steps: simTime +{done:0.000000}s (expected {10 * TickSeconds:0.000000}) "
            + $"world ticks={ticks} state={Main.Sim.State} -> {(ok ? "ok" : "FAILED")}");
        _tsPass &= ok;
        _tsPhase = 3;
    }
}
