using System;
using System.IO;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "stats" (plan 11): an emitter every 3 s on a 4 m belt that the PLC keeps on, and a remover at
/// the end. After a warm-up until the first item is removed the figures are cleared and measured for
/// 120 s of simulation time: the throughput must be within 5% of 60 ÷ 3 per minute and the lead time
/// within 10% of the distance from emitter to remover ÷ belt speed. Then 60 s of the belt 5 s on and
/// 5 s off: on-time 50% ± 2%. After Reset everything is zero, and the tab and its CSV export are checked.
/// </summary>
public partial class SelfTest
{
    private const double StatsInterval = 3.0;
    private const double StatsMeasureSeconds = 120.0;
    private const double StatsToggleSeconds = 60.0;
    private const float StatsEmitterX = -1.75f;
    private const float StatsRemoverX = 2.25f;

    private int _stPhase;
    private bool _stPass = true;
    private double _stPhaseStart;
    private string _stBelt = string.Empty;
    private string _stEmitter = string.Empty;
    private string _stRemover = string.Empty;
    private int _stResetFrames;

    private ProductionStats Stats => Main.Stats;

    /// <summary>Physics side: plays the PLC on simulation time and moves through the phases.</summary>
    private void RunStatsPhysics()
    {
        double now = Main.Sim.SimTime;
        bool belt = true;
        switch (_stPhase)
        {
            case 1 when Stats.RemovedCount > 0:
            {
                // Warm-up done: the line is full. The counts so far must match the parts' own counters.
                Remover? remover = Main.View.Find<Remover>(_stRemover);
                int emitted = Main.View.Emitters.Sum(e => e.EmittedCount);
                StatsCheck($"warm-up at {now:0.00} s: emitted {Stats.EmittedCount}/{emitted}, removed {Stats.RemovedCount}/{remover?.Count}",
                    Stats.EmittedCount == emitted && Stats.RemovedCount == remover?.Count);
                Main.ResetStats();
                _stPhaseStart = now;
                _stPhase = 2;
                break;
            }

            case 2 when now - _stPhaseStart >= StatsMeasureSeconds:
                JudgeStatsThroughput();
                Main.ResetStats();
                _stPhaseStart = now;
                _stPhase = 3;
                break;
            case 3:
                belt = (int)Math.Floor((now - _stPhaseStart) / 5.0) % 2 == 0;
                if (now - _stPhaseStart >= StatsToggleSeconds)
                {
                    JudgeStatsOnTime();
                    _stPhase = 4;
                }

                break;
        }

        var names = Main.Tags.ActuatorNames;
        Main.Tags.ApplyActuators(names.Select(name => name == _stEmitter || (name == _stBelt && belt)).ToArray());
    }

    /// <summary>Frame side: builds the line, then after the measurements resets and checks the tab.</summary>
    private void RunStatsFrame()
    {
        switch (_stPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("belt_conveyor_4m", 0.0f, 0.0f) is not null
                    && Editor.Place("emitter_gantry", StatsEmitterX, 0.0f) is not null
                    && SetParam("interval", StatsInterval)
                    && Editor.Place("remover_hood_1m0", StatsRemoverX, 0.0f) is not null;
                Editor.SetDesignMode(false);
                _stBelt = Main.View.DrivenTags.FirstOrDefault() ?? string.Empty;
                _stEmitter = Main.Scene.Items.FirstOrDefault(item => item.Asset == "emitter_gantry")?.Name ?? string.Empty;
                _stRemover = Main.View.Removers.FirstOrDefault().Name ?? string.Empty;
                GD.Print($"SELFTEST stats: belt={_stBelt} emitter={_stEmitter} remover={_stRemover} actuators={string.Join(", ", Main.Tags.ActuatorNames)}");
                StatsCheck("built", built && _stBelt.Length > 0 && _stEmitter.Length > 0 && _stRemover.Length > 0
                    && Main.View.DrivenTags.Count == 1);
                Main.Hud.ShowStats();
                Main.Camera.Frame(new Vector3(0.0f, 0.8f, 0.0f), 7.0f);
                Main.Sim.Run();
                _stPhase = 1;
                break;
            }

            case 4:
                CheckStatsTab(expectRows: true);
                SaveScreenshot(suffix: "_stats");
                Main.Sim.Reset();
                _stPhase = 5;
                break;
            case 5:
            {
                if (++_stResetFrames < 10)
                {
                    break;
                }

                RemoverStats? remover = Stats.Remover(_stRemover);
                bool zero = Stats is { RunTime: 0.0, EmittedCount: 0, RemovedCount: 0, FallenCount: 0, PerMinute: 0.0 }
                    && Stats.Actuators.Count == 0 && remover is { Count: 0, LeadTimeAverage: null };
                StatsCheck($"after reset: run time {Stats.RunTime:0.000} s, emitted {Stats.EmittedCount}, removed {Stats.RemovedCount}, "
                    + $"removers {Stats.Removers.Count}, actuators {Stats.Actuators.Count}", zero);
                CheckStatsTab(expectRows: false);
                _reported = true;
                GD.Print(_stPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_stPass ? 0 : 1);
                break;
            }
        }
    }

    private void JudgeStatsThroughput()
    {
        RemoverStats? remover = Stats.Remover(_stRemover);
        double expected = 60.0 / StatsInterval;
        double perMinute = remover?.PerMinute ?? 0.0;
        StatsCheck($"throughput {perMinute:0.00}/min over {Stats.RunTime:0.0} s ({remover?.Count} items), expected {expected:0.00} ± 5%",
            Math.Abs(perMinute - expected) <= expected * 0.05);
        StatsCheck($"total = remover ({Stats.PerMinute:0.00} vs {perMinute:0.00}), window {Stats.PerMinuteWindow:0.00}/min",
            Math.Abs(Stats.PerMinute - perMinute) < 1e-9 && Math.Abs(Stats.PerMinuteWindow - expected) <= expected * 0.05);

        // From the spawn point to where the front of a box enters the remover's volume.
        Remover? part = Main.View.Find<Remover>(_stRemover);
        double entry = StatsRemoverX - (part?.Size.X ?? 0.6f) / 2.0 - Box.Footprint / 2.0;
        double leadExpected = (entry - StatsEmitterX) / 0.6;
        double? lead = remover?.LeadTimeAverage;
        StatsCheck($"lead time {lead:0.00} s (min {remover?.LeadTimeMin:0.00}, max {remover?.LeadTimeMax:0.00}, n={remover?.LeadTimeCount}), "
            + $"expected {leadExpected:0.00} s ± 10% (length ÷ speed = {4.0 / 0.6:0.00} s)",
            lead is { } l && Math.Abs(l - leadExpected) <= leadExpected * 0.10);
        StatsCheck($"fallen {Stats.FallenCount}", Stats.FallenCount == 0);
    }

    private void JudgeStatsOnTime()
    {
        ActuatorStats? belt = Stats.Actuators.FirstOrDefault(a => a.Tag == _stBelt);
        StatsCheck($"on-time of {_stBelt}: {belt?.OnSeconds:0.000} s of {Stats.RunTime:0.000} s = {belt?.OnFraction * 100.0:0.0}%, expected 50 ± 2%",
            belt is { OnFraction: >= 0.48 and <= 0.52 });
    }

    /// <summary>The tab with rows (after measuring) or with only the empty remover (after Reset), and the CSV.</summary>
    private void CheckStatsTab(bool expectRows)
    {
        Ui.StatsView view = Main.Hud.StatsView;
        view.Refresh();
        (int removers, int driven) = view.RowCounts;
        bool rows = expectRows ? removers == 1 && driven == 1 : removers == 1 && driven == 0;
        StatsCheck($"tab: removers={removers} driven={driven} \"{view.SummaryText}\"", rows && view.SummaryText.StartsWith("Looptijd", StringComparison.Ordinal));

        string? path = view.Export();
        string[] lines = path is null ? Array.Empty<string>() : File.ReadAllLines(path);
        bool csv = lines.Length == (expectRows ? 5 : 3)
            && lines[0] == "Kind;Name;Count;PerMinute;PerMinuteLast60s;LeadTimeAvg;LeadTimeMin;LeadTimeMax;OnSeconds;OnPercent"
            && lines[1].StartsWith("total;", StringComparison.Ordinal)
            && lines[2].StartsWith("remover;" + _stRemover + ";", StringComparison.Ordinal);
        StatsCheck($"CSV {path}: {lines.Length} lines; {string.Join(" | ", lines.Skip(1))}", csv);
    }

    private void StatsCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST stats {what}: {(ok ? "ok" : "FAILED")}");
        _stPass &= ok;
    }
}
