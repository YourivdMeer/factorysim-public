using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "bidir" (plan 12): a 4 m belt with <c>twoWay</c>, a photo-eye near each end and an emitter that
/// makes one box; the PLC runs it forward to the far eye and back to the near eye, three times. Then both
/// outputs on: the box stands still and the status bar reports the conflict. Then a bistable pusher: a
/// 0.2 s pulse on each coil moves it all the way and it stays; both coils on: it does not move and reports
/// the conflict. Reset: belt still, pusher back, no conflict.
/// </summary>
public partial class SelfTest
{
    private const int BidirTrips = 3;
    private const double BidirTimeout = 90.0;

    private int _biPhase;
    private bool _biPass = true;
    private double _biPhaseStart;
    private string _biBelt = string.Empty;
    private string _biNear = string.Empty;
    private string _biFar = string.Empty;
    private string _biPusher = string.Empty;
    private bool _biBackward;
    private int _biLegs;
    private Vector3 _biBoxAt;
    private bool _biConflictSeen;
    private int _biResetFrames;

    private Box? BidirBox => GetTree().GetNodesInGroup(Box.Group).OfType<Box>().FirstOrDefault(box => !box.IsQueuedForDeletion());

    /// <summary>Physics side: plays the PLC on simulation time.</summary>
    private void RunBidirPhysics()
    {
        double now = Main.Sim.SimTime;
        double inPhase = now - _biPhaseStart;
        (_, int fallen) = Main.CountBoxes();
        _maxFallen = Math.Max(_maxFallen, fallen);
        var on = new HashSet<string>();
        switch (_biPhase)
        {
            case 1:
            {
                // Back and forth between the two eyes.
                if (Main.View.Emitters.Sum(e => e.EmittedCount) == 0)
                {
                    on.Add(EmitterTagName());
                }

                if (!_biBackward && PanelSensor(_biFar))
                {
                    _biBackward = true;
                    _biLegs++;
                }
                else if (_biBackward && PanelSensor(_biNear))
                {
                    _biBackward = false;
                    _biLegs++;
                }

                if (_biLegs >= BidirTrips * 2 || inPhase > BidirTimeout)
                {
                    BidirCheck($"{_biLegs / 2} round trips in {inPhase:0.0} s, fallen {_maxFallen}", _biLegs >= BidirTrips * 2 && _maxFallen == 0);
                    NextBidirPhase(2, now);
                    break;
                }

                on.Add(_biBackward ? SceneTags.ConveyorReverse(_biBelt) : _biBelt);
                break;
            }

            case 2:
                // Both outputs on: the belt must stop.
                on.Add(_biBelt);
                on.Add(SceneTags.ConveyorReverse(_biBelt));
                if (inPhase >= 0.2 && _biBoxAt == Vector3.Zero)
                {
                    _biBoxAt = BidirBox?.GlobalPosition ?? Vector3.Inf;
                }

                _biConflictSeen |= inPhase >= 0.2 && Main.Shell.StatusWarning.Text.Contains("vooruit en achteruit", StringComparison.Ordinal);
                if (inPhase >= 1.2)
                {
                    float moved = (BidirBox?.GlobalPosition ?? Vector3.Inf).DistanceTo(_biBoxAt);
                    Conveyor? belt = Main.View.Find<Conveyor>(_biBelt);
                    BidirCheck($"both outputs on: box moved {moved * 1000.0f:0.0} mm in 1 s, belt running={belt?.Running}, status \"{Main.Shell.StatusWarning.Text}\"",
                        moved < 0.01f && belt is { Running: false, Conflict: true } && _biConflictSeen);
                    NextBidirPhase(3, now);
                }

                break;
            case 3:
            {
                // A 0.2 s pulse on the extend coil: all the way out, and it stays out.
                Pusher? pusher = Main.View.Find<Pusher>(_biPusher);
                if (inPhase < 0.2)
                {
                    on.Add(_biPusher);
                }

                if (inPhase >= 1.5)
                {
                    BidirCheck($"pulse out: front={PanelSensor(SceneTags.PusherFront(_biPusher))} back={PanelSensor(SceneTags.PusherBack(_biPusher))} "
                        + $"head {pusher?.HeadPosition:0.000} m, coils off",
                        PanelSensor(SceneTags.PusherFront(_biPusher)) && !PanelSensor(SceneTags.PusherBack(_biPusher)));
                    NextBidirPhase(4, now);
                }

                break;
            }

            case 4:
                // A pulse on the retract coil: all the way back.
                if (inPhase < 0.2)
                {
                    on.Add(SceneTags.PusherRetract(_biPusher));
                }

                if (inPhase >= 1.5)
                {
                    BidirCheck($"pulse in: back={PanelSensor(SceneTags.PusherBack(_biPusher))}", PanelSensor(SceneTags.PusherBack(_biPusher)));
                    NextBidirPhase(5, now);
                }

                break;
            case 5:
            {
                // Both coils on from the back: no movement, conflict.
                Pusher? pusher = Main.View.Find<Pusher>(_biPusher);
                on.Add(_biPusher);
                on.Add(SceneTags.PusherRetract(_biPusher));
                _biConflictSeen |= Main.Shell.StatusWarning.Text.Contains("uit en in", StringComparison.Ordinal);
                if (inPhase >= 1.0)
                {
                    BidirCheck($"both coils on: head {pusher?.HeadPosition:0.000} m, conflict={pusher?.Conflict}, status \"{Main.Shell.StatusWarning.Text}\"",
                        pusher is { HeadPosition: 0.0, Conflict: true, AtBack: true } && _biConflictSeen);
                    NextBidirPhase(6, now);
                }

                // Keep both on into phase 6, so the reset has a conflict to clear.
                break;
            }

            case 6:
                on.Add(_biBelt);
                on.Add(SceneTags.ConveyorReverse(_biBelt));
                on.Add(_biPusher);
                on.Add(SceneTags.PusherRetract(_biPusher));
                break;
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>Frame side: builds the scene, and resets at the end.</summary>
    private void RunBidirFrame()
    {
        switch (_biPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = Editor.Place("belt_conveyor_4m", 0.0f, 0.0f) is not null
                    && SetParam(SceneTags.TwoWay, 1.0)
                    && Editor.Place("emitter_gantry", -1.75f, 0.0f) is not null
                    && Editor.Place("diffuse_sensor", -1.0f, -0.6f) is not null
                    && Editor.Place("diffuse_sensor", 1.5f, -0.6f) is not null
                    && Editor.Place("belt_conveyor_2m", 0.0f, -3.0f) is not null
                    && Editor.Place("pusher", 0.0f, -3.48f) is not null
                    && SetParam(SceneTags.Bistable, 1.0);
                Editor.SetDesignMode(false);
                SceneItem[] eyes = Main.Scene.Items.Where(item => item.Asset == "diffuse_sensor").OrderBy(item => item.X).ToArray();
                _biNear = eyes.FirstOrDefault()?.Name ?? string.Empty;
                _biFar = eyes.LastOrDefault()?.Name ?? string.Empty;
                _biBelt = Main.Scene.Items.FirstOrDefault(item => item.Asset == "belt_conveyor_4m")?.Name ?? string.Empty;
                _biPusher = Main.Scene.Items.FirstOrDefault(item => item.Asset == "pusher")?.Name ?? string.Empty;
                string[] wanted = { _biBelt, SceneTags.ConveyorReverse(_biBelt), _biPusher, SceneTags.PusherRetract(_biPusher) };
                GD.Print($"SELFTEST bidir: actuators={string.Join(", ", Main.Tags.ActuatorNames)} near={_biNear} far={_biFar}");
                BidirCheck("built", built && eyes.Length == 2 && wanted.All(Main.Tags.ActuatorNames.Contains)
                    && Main.View.Find<Pusher>(_biPusher) is { Bistable: true });
                Main.Camera.Frame(new Vector3(0.0f, 0.8f, -1.5f), 8.0f);
                Main.Sim.Run();
                NextBidirPhase(1, Main.Sim.SimTime);
                break;
            }

            case 6:
                SaveScreenshot(suffix: "_bidir");
                Main.Sim.Reset();
                _biPhase = 7;
                break;
            case 7:
            {
                if (++_biResetFrames < 10)
                {
                    break;
                }

                Conveyor? belt = Main.View.Find<Conveyor>(_biBelt);
                Pusher? pusher = Main.View.Find<Pusher>(_biPusher);
                BidirCheck($"reset: belt running={belt?.Running} backward={belt?.Backward}, pusher back={pusher?.AtBack} conflict={pusher?.Conflict}, "
                    + $"status \"{Main.Shell.StatusWarning.Text}\"",
                    belt is { Running: false, Backward: false, Conflict: false } && pusher is { AtBack: true, Conflict: false }
                    && Main.Shell.StatusWarning.Text.Length == 0);
                _reported = true;
                GD.Print(_biPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_biPass ? 0 : 1);
                break;
            }
        }
    }

    private string EmitterTagName() => Main.Scene.Items.FirstOrDefault(item => item.Asset == "emitter_gantry")?.Name ?? string.Empty;

    private void NextBidirPhase(int phase, double now)
    {
        _biPhase = phase;
        _biPhaseStart = now;
        _biConflictSeen = false;
    }

    private void BidirCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST bidir {what}: {(ok ? "ok" : "FAILED")}");
        _biPass &= ok;
    }
}
