using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// The floor plan on a card of the scene gallery (plan 20): the floor as a plane and every item as its
/// turned outline, coloured per kind, drawn from above (x to the right, z downwards) and scaled to fit.
/// </summary>
public partial class FloorPlanView : Control
{
    private static readonly Color FloorColour = new(0.24f, 0.25f, 0.28f);
    private static readonly Color Background = new(0.12f, 0.13f, 0.15f);

    private SceneSummary? _summary;

    /// <summary>The scene to draw; null draws an empty plan.</summary>
    public SceneSummary? Summary
    {
        get => _summary;
        set
        {
            _summary = value;
            QueueRedraw();
        }
    }

    public static Color ColourOf(FootprintKind kind) => kind switch
    {
        FootprintKind.Transport => new Color(0.55f, 0.62f, 0.74f),
        FootprintKind.Sensor => new Color(0.98f, 0.80f, 0.22f),
        FootprintKind.Station => new Color(0.96f, 0.52f, 0.22f),
        _ => new Color(0.46f, 0.78f, 0.52f),
    };

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_summary is null)
        {
            return;
        }

        // Everything that has to fit: the floor and the outlines.
        float halfLength = (float)_summary.Floor.Length / 2.0f;
        float halfWidth = (float)_summary.Floor.Width / 2.0f;
        var bounds = new Rect2(-halfLength, -halfWidth, 2.0f * halfLength, 2.0f * halfWidth);
        foreach (FloorPoint corner in _summary.Footprints.SelectMany(footprint => footprint.Corners))
        {
            bounds = bounds.Expand(new Vector2((float)corner.X, (float)corner.Z));
        }

        const float margin = 6.0f;
        float scale = Mathf.Min((Size.X - 2.0f * margin) / bounds.Size.X, (Size.Y - 2.0f * margin) / bounds.Size.Y);
        Vector2 offset = Size / 2.0f - bounds.GetCenter() * scale;
        Vector2 ToView(double x, double z) => offset + new Vector2((float)x, (float)z) * scale;

        DrawRect(new Rect2(ToView(-halfLength, -halfWidth), new Vector2(2.0f * halfLength, 2.0f * halfWidth) * scale), FloorColour);
        foreach (ItemFootprint footprint in _summary.Footprints)
        {
            Vector2[] points = footprint.Corners.Select(corner => ToView(corner.X, corner.Z)).ToArray();
            DrawColoredPolygon(points, ColourOf(footprint.Kind));
        }
    }
}
