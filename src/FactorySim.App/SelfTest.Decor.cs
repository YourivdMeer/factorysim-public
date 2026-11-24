using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Self-test "decor" (plan 13): the six decor pieces in a row, with an emitter above the platform, the
/// switchboard, the column and the stairs, and a belt that pushes a box against a safeguard turned across
/// it. Each emitter makes one low box. Checks: the box on the platform lies on the deck; the boxes on the
/// switchboard and the column never get below the top within the footprint; the box on the stairs never
/// gets below the line of the treads; the box on the belt stays in front of the safeguard; nothing falls
/// through the floor. The handrail only stands in the row.
/// </summary>
public partial class SelfTest
{
    private const double DecorTimeout = 30.0;
    private const double DecorSettle = 4.0;

    private static readonly (string Asset, float X, float Z, float Yaw, float DropHeight)[] DecorRow =
    {
        ("platform_2x2", -5.5f, 0.0f, 0.0f, 1.5f),
        ("electric_switchboard", -3.0f, 0.0f, 0.0f, 1.6f),
        ("column", -1.5f, 0.0f, 0.0f, 1.5f),
        ("stairs", 0.5f, 0.0f, 0.0f, 1.8f),
        ("handrail_2m", 3.0f, 0.0f, 0.0f, 0.0f),
        ("safeguard_2m", DecorSafeguardX, 0.0f, 90.0f, 0.0f),
    };

    private const float DecorBeltX = 5.5f;          // a 2 m belt from x = 4.5 to 6.5, running towards +x
    private const float DecorBeltEmitterX = 4.8f;
    private const float DecorSafeguardX = 6.55f;    // the default floor ends at x = 7

    private int _decPhase;
    private bool _decPass = true;
    private double _decAllSpawnedAt = -1.0;
    private string _decBelt = string.Empty;
    private readonly Dictionary<string, string> _decEmitterOver = new();     // emitter name -> asset under it
    private readonly Dictionary<string, Box> _decBoxes = new();              // asset -> its box
    private readonly HashSet<string> _decViolations = new();
    private float _decLowestCentre = float.MaxValue;
    private float _decBeltBoxMaxX = float.MinValue;
    private bool _decHooked;

    /// <summary>Physics side: lets each emitter make one box, runs the belt and watches the boxes.</summary>
    private void RunDecorPhysics()
    {
        if (_decPhase != 1)
        {
            return;
        }

        double now = Main.Sim.SimTime;
        var on = new HashSet<string> { _decBelt };
        foreach ((string emitter, string asset) in _decEmitterOver)
        {
            if (!_decBoxes.ContainsKey(asset))
            {
                on.Add(emitter);
            }
        }

        foreach ((string asset, Box box) in _decBoxes)
        {
            if (!IsInstanceValid(box) || box.IsQueuedForDeletion())
            {
                _decViolations.Add($"{asset}: box removed");
                continue;
            }

            WatchDecorBox(asset, box);
        }

        if (_decAllSpawnedAt < 0.0 && _decBoxes.Count == _decEmitterOver.Count)
        {
            _decAllSpawnedAt = now;
        }

        bool settled = _decAllSpawnedAt >= 0.0 && now - _decAllSpawnedAt >= DecorSettle;
        if (settled || now > DecorTimeout)
        {
            FinishDecor(now);
            on.Clear();
        }

        Main.Tags.ApplyActuators(Main.Tags.ActuatorNames.Select(on.Contains).ToArray());
    }

    /// <summary>Checks one box against the piece it was dropped on, every tick.</summary>
    private void WatchDecorBox(string asset, Box box)
    {
        Vector3 at = box.GlobalPosition;
        _decLowestCentre = Math.Min(_decLowestCentre, at.Y);
        (_, float x, float z, _, _) = DecorRow.FirstOrDefault(row => row.Asset == asset);
        float dx = at.X - x;
        float dz = at.Z - z;
        float bottom = at.Y - box.Height / 2.0f;
        switch (asset)
        {
            case "electric_switchboard":
            case "column":
            {
                CatalogAsset piece = Main.Catalog.Get(asset);
                bool over = dx > piece.BoundsMin[0] + 0.02 && dx < piece.BoundsMax[0] - 0.02
                    && dz > piece.BoundsMin[2] + 0.02 && dz < piece.BoundsMax[2] - 0.02;
                double top = piece.Colliders?.Max(c => c.Position[1] + c.Size[1] / 2.0) ?? piece.BoundsMax[1];
                if (over && bottom < top - 0.05)
                {
                    _decViolations.Add($"{asset}: box inside the piece, bottom {bottom:0.000} m at ({dx:0.00}, {dz:0.00})");
                }

                break;
            }

            case "stairs":
                // The treads rise 0.2 m per 0.25 m towards +x; the line through their back edges is
                // y = 0.5 + 0.8 x. A box on the stairs keeps its centre above it.
                if (Math.Abs(dx) < 0.6f && Math.Abs(dz) < 0.45f && at.Y < 0.45f + 0.8f * dx)
                {
                    _decViolations.Add($"stairs: box below the treads, centre {at.Y:0.000} m at x {dx:0.00}");
                }

                break;
            case "belt":
                _decBeltBoxMaxX = Math.Max(_decBeltBoxMaxX, at.X);
                break;
        }
    }

    private void FinishDecor(double now)
    {
        DecorCheck($"{_decBoxes.Count} of {_decEmitterOver.Count} boxes made in {now:0.0} s", _decBoxes.Count == _decEmitterOver.Count);

        Box? deck = _decBoxes.GetValueOrDefault("platform_2x2");
        double deckHeight = Main.Catalog.Get("platform_2x2").Param("deck_height", 1.0);
        float deckBottom = deck is null ? float.NaN : deck.GlobalPosition.Y - deck.Height / 2.0f;
        DecorCheck($"platform: box bottom {deckBottom:0.000} m, deck {deckHeight:0.000} m",
            deck is not null && Math.Abs(deckBottom - deckHeight) <= 0.02);

        foreach (string asset in new[] { "electric_switchboard", "column", "stairs" })
        {
            Box? box = _decBoxes.GetValueOrDefault(asset);
            string[] hits = _decViolations.Where(v => v.StartsWith(asset, StringComparison.Ordinal)).ToArray();
            DecorCheck($"{asset}: box ends at {box?.GlobalPosition.ToString() ?? "-"}{(hits.Length > 0 ? ", " + hits[0] : string.Empty)}",
                box is not null && hits.Length == 0);
        }

        Box? pushed = _decBoxes.GetValueOrDefault("belt");
        DecorCheck($"safeguard: box got to x {_decBeltBoxMaxX:0.000} m (safeguard at {DecorSafeguardX:0.00}), ends at {pushed?.GlobalPosition.ToString() ?? "-"}",
            pushed is not null && _decBeltBoxMaxX > DecorBeltX + 0.6f && _decBeltBoxMaxX + 0.15f < DecorSafeguardX + 0.1f && pushed.GlobalPosition.Y > 0.8f);

        DecorCheck($"floor: lowest box centre {_decLowestCentre:0.000} m", _decLowestCentre > 0.05f);
        _decPhase = 2;
    }

    /// <summary>Frame side: builds the row, then takes the screenshot and reports.</summary>
    private void RunDecorFrame()
    {
        switch (_decPhase)
        {
            case 0:
            {
                Editor.SetDesignMode(true);
                Editor.NewScene();
                bool built = true;
                foreach ((string asset, float x, float z, float yaw, float drop) in DecorRow)
                {
                    built &= Editor.Place(asset, x, z, yaw) is not null;
                    if (drop > 0.0f)
                    {
                        SceneItem? emitter = Editor.Place("emitter_gantry", x, z);
                        built &= emitter is not null && SetParam("highRatio", 0.0);
                        Editor.SetSelectedHeight(drop);
                        if (emitter is not null)
                        {
                            _decEmitterOver[emitter.Name] = asset;
                        }
                    }
                }

                SceneItem? belt = Editor.Place("belt_conveyor_2m", DecorBeltX, 0.0f);
                SceneItem? beltEmitter = Editor.Place("emitter_gantry", DecorBeltEmitterX, 0.0f);
                built &= belt is not null && beltEmitter is not null && SetParam("highRatio", 0.0);
                Editor.SetDesignMode(false);
                _decBelt = belt?.Name ?? string.Empty;
                if (beltEmitter is not null)
                {
                    _decEmitterOver[beltEmitter.Name] = "belt";
                }

                if (!_decHooked)
                {
                    _decHooked = true;
                    Main.View.ItemEmitted += (emitter, box) =>
                    {
                        if (_decEmitterOver.TryGetValue(emitter, out string? asset) && !_decBoxes.ContainsKey(asset))
                        {
                            _decBoxes[asset] = box;
                        }
                    };
                }

                bool passive = DecorRow.All(row => Main.Catalog.Get(row.Asset).Behaviour == AssetBehaviours.Passive);
                bool heights = Main.Scene.Items.Where(item => item.Asset == "emitter_gantry").All(item => item.Y > 0.7);
                GD.Print($"SELFTEST decor: emitters={string.Join(", ", _decEmitterOver.Select(p => $"{p.Key}->{p.Value}"))} belt={_decBelt}");
                DecorCheck("built", built && passive && heights && _decEmitterOver.Count == 5 && Main.Tags.ActuatorNames.Contains(_decBelt));
                Main.Camera.Frame(new Vector3(0.0f, 1.0f, 0.0f), 15.0f);
                Main.Sim.Run();
                _decPhase = 1;
                break;
            }

            case 2:
                SaveScreenshot(suffix: "_decor");
                _reported = true;
                GD.Print(_decPass ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");
                GetTree().Quit(_decPass ? 0 : 1);
                break;
        }
    }

    private void DecorCheck(string what, bool ok)
    {
        GD.Print($"SELFTEST decor {what}: {(ok ? "ok" : "FAILED")}");
        _decPass &= ok;
    }
}
