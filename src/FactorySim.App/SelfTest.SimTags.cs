using System;
using System.Linq;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Second half of self-test "controls" (plan 14): the optional simulation tags. The acceptance scene has
/// neither tag; after switching both on (coils 7 and 8) Run gives running on and paused off, Pause the
/// other way round, and a Reset gives Sim reset for one second of real time while Sim paused stays off.
/// Times are wall-clock (<see cref="Time.GetTicksMsec"/>), like the pulse itself.
/// </summary>
public partial class SelfTest
{
    private const double SimTagsPulseTolerance = 0.1;

    private bool _simTagsAbsent;
    private bool _simTagsOk = true;
    private int _simTagsPhase;
    private double _simTagsPhaseAt;
    private double _simTagsResetAt;
    private double _simTagsLastHigh = -1.0;
    private double _simTagsFirstLow = -1.0;
    private bool _simTagsPausedDuringPulse;

    private static double WallSeconds() => Time.GetTicksMsec() / 1000.0;

    private bool? SimTagValue(string name) =>
        Main.Tags.Snapshot().FirstOrDefault(tag => tag.Name == name) is { } state ? state.Value : null;

    private void SimTagsCheck(string what, bool ok, string detail)
    {
        GD.Print($"SELFTEST controls sim tags {what}: {detail}: {(ok ? "ok" : "FAILED")}");
        _simTagsOk &= ok;
    }

    /// <summary>Called once at the start of "controls", on the acceptance scene as loaded.</summary>
    private void CheckSimTagsAbsent()
    {
        _simTagsAbsent = Main.Tags.Find(SceneTags.SimPaused) is null && Main.Tags.Find(SceneTags.SimReset) is null
            && Main.Scene.SimTags.Count == 0;
        SimTagsCheck("absent without the field", _simTagsAbsent, $"tags={Main.TagMap.Entries.Count}");
    }

    /// <summary>Runs after the first half of "controls"; returns true when done.</summary>
    private bool RunSimTags()
    {
        double now = WallSeconds();
        switch (_simTagsPhase)
        {
            case 0:
            {
                string? error = Editor.SetSimTags(new[] { SceneTags.SimPaused, SceneTags.SimReset });
                TagMapEntry? paused = Main.TagMap.Entries.FirstOrDefault(tag => tag.Name == SceneTags.SimPaused);
                TagMapEntry? reset = Main.TagMap.Entries.FirstOrDefault(tag => tag.Name == SceneTags.SimReset);
                TagMapEntry? running = Main.TagMap.Entries.FirstOrDefault(tag => tag.Name == SceneTags.SimRunning);
                SimTagsCheck("switched on", error is null && paused is { Area: ModbusArea.Coil, Offset: 7 }
                    && reset is { Area: ModbusArea.Coil, Offset: 8 } && running is { Offset: 6 },
                    $"error={error ?? "none"} paused={paused?.IecAddress} reset={reset?.IecAddress} running={running?.IecAddress}");
                Main.Sim.Run();
                _simTagsPhase = 1;
                _simTagsPhaseAt = now;
                return false;
            }

            case 1 when now - _simTagsPhaseAt >= 1.5:     // past the pulse the rebuild gave
                SimTagsCheck("run", SimTagValue(SceneTags.SimRunning) == true && SimTagValue(SceneTags.SimPaused) == false
                    && SimTagValue(SceneTags.SimReset) == false,
                    $"running={SimTagValue(SceneTags.SimRunning)} paused={SimTagValue(SceneTags.SimPaused)} reset={SimTagValue(SceneTags.SimReset)}");
                Main.Sim.Pause();
                _simTagsPhase = 2;
                _simTagsPhaseAt = now;
                return false;

            case 2 when now - _simTagsPhaseAt >= 0.5:
                SimTagsCheck("pause", SimTagValue(SceneTags.SimPaused) == true && SimTagValue(SceneTags.SimRunning) == false,
                    $"running={SimTagValue(SceneTags.SimRunning)} paused={SimTagValue(SceneTags.SimPaused)}");
                Main.Sim.Run();
                _simTagsPhase = 3;
                _simTagsPhaseAt = now;
                return false;

            case 3 when now - _simTagsPhaseAt >= 0.5:
                _simTagsResetAt = WallSeconds();
                Main.Sim.Reset();
                _simTagsPhase = 4;
                return false;

            case 4:
            {
                bool high = SimTagValue(SceneTags.SimReset) == true;
                if (high)
                {
                    _simTagsLastHigh = now;
                }
                else if (_simTagsLastHigh >= 0.0 && _simTagsFirstLow < 0.0)
                {
                    _simTagsFirstLow = now;
                }

                _simTagsPausedDuringPulse |= SimTagValue(SceneTags.SimPaused) == true;
                if (now - _simTagsResetAt < 2.0)
                {
                    return false;
                }

                // The pulse ends somewhere between the last frame it was seen high and the first frame it
                // was seen low; that interval must overlap 1 s +- 0.1 s.
                double lastHigh = _simTagsLastHigh - _simTagsResetAt;
                double firstLow = _simTagsFirstLow - _simTagsResetAt;
                bool length = _simTagsLastHigh >= 0.0 && _simTagsFirstLow >= 0.0
                    && lastHigh <= SimResetPulse.Duration + SimTagsPulseTolerance
                    && firstLow >= SimResetPulse.Duration - SimTagsPulseTolerance;
                SimTagsCheck("reset pulse", length, $"last high after {lastHigh:0.000} s, first low after {firstLow:0.000} s");
                SimTagsCheck("paused stays off after reset", !_simTagsPausedDuringPulse
                    && Main.Sim.State == SimState.Stopped, $"pausedSeen={_simTagsPausedDuringPulse} state={Main.Sim.State}");
                return true;
            }
        }

        return false;
    }
}
