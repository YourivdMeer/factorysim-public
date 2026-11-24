using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// The lanes of the trace, drawn with _Draw: names on the left, a square wave per bool tag and a
/// stepped line per Int tag, a time axis below. Wheel zooms around the mouse, dragging pans,
/// a click sets cursor A, Shift-click cursor B, a click on a name switches its lane on or off.
/// Only what falls in the window is drawn; several changes in one pixel column become one stripe.
/// </summary>
public partial class TraceCanvas : Control
{
    private const float NameWidth = 230.0f;
    private const float RowHeight = 22.0f;
    private const float AxisHeight = 20.0f;
    private const double MinSpan = 0.01;
    private const double MaxSpan = 600.0;
    private const float DragThreshold = 4.0f;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly Color LaneShade = new(1.0f, 1.0f, 1.0f, 0.035f);
    private static readonly Color GridColour = new(1.0f, 1.0f, 1.0f, 0.08f);
    private static readonly Color CursorA = new(0.36f, 0.62f, 0.98f);
    private static readonly Color CursorB = new(0.95f, 0.45f, 0.85f);

    private readonly TagRecorder _recorder;
    private string[] _lanes = Array.Empty<string>();
    private readonly HashSet<string> _unchecked = new(StringComparer.Ordinal);

    private double _span = 10.0;
    private double _end = 10.0;
    private bool _live = true;
    private double? _cursorA;
    private double? _cursorB;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressAt;
    private double _pressEnd;

    public TraceCanvas(TagRecorder recorder)
    {
        _recorder = recorder;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    /// <summary>Raised when Live goes on or off from inside the canvas (by panning).</summary>
    public event Action<bool>? LiveChanged;

    public IReadOnlyList<string> Lanes => _lanes;

    public IEnumerable<string> CheckedLanes => _lanes.Where(lane => !_unchecked.Contains(lane));

    /// <summary>Follow the present: the right edge is the last recorded tick.</summary>
    public bool Live
    {
        get => _live;
        set
        {
            _live = value;
            QueueRedraw();
        }
    }

    public void SetLanes(string[] lanes)
    {
        _lanes = lanes;
        CustomMinimumSize = new Vector2(0.0f, lanes.Length * RowHeight + AxisHeight);
        QueueRedraw();
    }

    /// <summary>Top line of the view: cursors and the time between them.</summary>
    public string CursorText()
    {
        if (_cursorA is null && _cursorB is null)
        {
            return "Klik: cursor A · Shift-klik: cursor B · wiel: zoom · slepen: schuiven · klik op een naam: baan aan/uit";
        }

        string text = string.Empty;
        if (_cursorA is { } a)
        {
            text += $"A = {Seconds(a)}";
        }

        if (_cursorB is { } b)
        {
            text += (text.Length > 0 ? "   " : string.Empty) + $"B = {Seconds(b)}";
        }

        if (_cursorA is { } a2 && _cursorB is { } b2)
        {
            text += $"   Δ = {((b2 - a2) * 1000.0).ToString("0.0", Dutch)} ms";
        }

        return text;
    }

    // ---------------------------------------------------------------- drawing

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        const int fontSize = 13;
        double end = WindowEnd();
        double start = end - _span;
        float plotWidth = Math.Max(1.0f, Size.X - NameWidth);

        for (int i = 0; i < _lanes.Length; i++)
        {
            string tag = _lanes[i];
            float top = i * RowHeight;
            if (i % 2 == 0)
            {
                DrawRect(new Rect2(0.0f, top, Size.X, RowHeight), LaneShade);
            }

            bool on = !_unchecked.Contains(tag);
            string name = (on ? "☑ " : "☐ ") + tag + CursorValues(tag);
            DrawString(font, new Vector2(4.0f, top + 15.0f), name, HorizontalAlignment.Left, NameWidth - 8.0f, fontSize,
                on ? Colors.White : Shell.Muted);
            if (on)
            {
                DrawLane(tag, top, start, end, plotWidth);
            }
        }

        DrawAxis(font, fontSize, start, end, plotWidth);
        DrawCursor(_cursorA, CursorA, start, plotWidth);
        DrawCursor(_cursorB, CursorB, start, plotWidth);
    }

    private void DrawLane(string tag, float top, double start, double end, float plotWidth)
    {
        double origin = _recorder.Origin;
        if (double.IsNaN(origin) || origin > end)
        {
            return;
        }

        double from = Math.Max(start, origin);
        double to = Math.Min(end, _recorder.End);
        int? first = _recorder.ValueAt(tag, from);
        if (first is null)
        {
            return;
        }

        IReadOnlyList<TagChange> changes = _recorder.Changes(tag, from, to);
        bool analog = IsAnalog(tag, first.Value, changes);

        // Int tags: scale the line between the lowest and highest value in view.
        int low = first.Value;
        int high = first.Value;
        foreach (TagChange change in changes)
        {
            low = Math.Min(low, change.Value);
            high = Math.Max(high, change.Value);
        }

        float yHigh = top + 4.0f;
        float yLow = top + RowHeight - 5.0f;
        float Level(int value) => analog
            ? high == low ? (yHigh + yLow) / 2.0f : yLow - (yLow - yHigh) * (value - low) / (float)(high - low)
            : value != 0 ? yHigh : yLow;
        Color Colour(int value, bool forced) => forced ? Shell.Warn : analog || value != 0 ? Shell.Good : Shell.Muted;

        int current = first.Value;
        bool forcedNow = _recorder.ForcedAt(tag, from);
        float x = X(from, start, plotWidth);
        int column = int.MinValue;
        bool columnFull = false;
        foreach (TagChange change in changes)
        {
            float cx = X(change.Time, start, plotWidth);
            int pixel = (int)cx;
            if (pixel == column)
            {
                // Several changes in one pixel column: one full-height stripe.
                if (!columnFull)
                {
                    DrawLine(new Vector2(pixel + 0.5f, yHigh), new Vector2(pixel + 0.5f, yLow), Colour(1, change.Forced));
                    columnFull = true;
                }
            }
            else
            {
                if (cx > x)
                {
                    DrawLine(new Vector2(x, Level(current)), new Vector2(cx, Level(current)), Colour(current, forcedNow), 2.0f);
                }

                if (change.Value != current)
                {
                    DrawLine(new Vector2(cx, Level(current)), new Vector2(cx, Level(change.Value)), Colour(1, change.Forced));
                }

                column = pixel;
                columnFull = false;
            }

            current = change.Value;
            forcedNow = change.Forced;
            x = cx;
        }

        float right = X(to, start, plotWidth);
        if (right > x)
        {
            DrawLine(new Vector2(x, Level(current)), new Vector2(right, Level(current)), Colour(current, forcedNow), 2.0f);
        }
    }

    /// <summary>A tag is drawn as a stepped line when any value in view is not 0 or 1, or the store says Int.</summary>
    private bool IsAnalog(string tag, int first, IReadOnlyList<TagChange> changes) =>
        AnalogTags.Contains(tag) || first is < 0 or > 1 || changes.Any(c => c.Value is < 0 or > 1);

    /// <summary>Names of the Int tags; set by the trace view from the tag map.</summary>
    public HashSet<string> AnalogTags { get; } = new(StringComparer.Ordinal);

    private void DrawAxis(Font font, int fontSize, double start, double end, float plotWidth)
    {
        float y = _lanes.Length * RowHeight;
        DrawLine(new Vector2(NameWidth, y), new Vector2(Size.X, y), Shell.Muted);
        double step = NiceStep(_span / 8.0);
        double first = Math.Ceiling(start / step) * step;
        string format = step >= 1.0 ? "0" : step >= 0.1 ? "0.0" : step >= 0.01 ? "0.00" : "0.000";
        for (double t = first; t <= end + step * 1e-6; t += step)
        {
            float x = X(t, start, plotWidth);
            DrawLine(new Vector2(x, 0.0f), new Vector2(x, y), GridColour);
            DrawLine(new Vector2(x, y), new Vector2(x, y + 4.0f), Shell.Muted);
            DrawString(font, new Vector2(x + 2.0f, y + 15.0f), t.ToString(format, Dutch) + " s", HorizontalAlignment.Left, -1, 11, Shell.Muted);
        }
    }

    private void DrawCursor(double? time, Color colour, double start, float plotWidth)
    {
        if (time is not { } t)
        {
            return;
        }

        float x = X(t, start, plotWidth);
        if (x >= NameWidth && x <= Size.X)
        {
            DrawLine(new Vector2(x, 0.0f), new Vector2(x, _lanes.Length * RowHeight), colour, 1.5f);
        }
    }

    private string CursorValues(string tag)
    {
        string text = string.Empty;
        if (_cursorA is { } a)
        {
            text += "  A:" + (_recorder.ValueAt(tag, a)?.ToString(CultureInfo.InvariantCulture) ?? "–");
        }

        if (_cursorB is { } b)
        {
            text += "  B:" + (_recorder.ValueAt(tag, b)?.ToString(CultureInfo.InvariantCulture) ?? "–");
        }

        return text;
    }

    private static double NiceStep(double raw)
    {
        double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        double fraction = raw / magnitude;
        double nice = fraction <= 1.0 ? 1.0 : fraction <= 2.0 ? 2.0 : fraction <= 5.0 ? 5.0 : 10.0;
        return nice * magnitude;
    }

    private static string Seconds(double t) => t.ToString("0.000", Dutch) + " s";

    private float X(double time, double start, float plotWidth) => NameWidth + (float)((time - start) / _span * plotWidth);

    private double TimeAt(float x, float plotWidth) => WindowEnd() - _span + (x - NameWidth) / plotWidth * _span;

    /// <summary>Right edge of the view: the last tick while Live (at least one span from zero), else where it was panned to.</summary>
    private double WindowEnd()
    {
        if (_live)
        {
            double last = double.IsNaN(_recorder.End) ? 0.0 : _recorder.End;
            double origin = double.IsNaN(_recorder.Origin) ? 0.0 : _recorder.Origin;
            _end = Math.Max(last, origin + _span);
        }

        return _end;
    }

    // ---------------------------------------------------------------- input

    public override void _GuiInput(InputEvent @event)
    {
        float plotWidth = Math.Max(1.0f, Size.X - NameWidth);
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? 0.8 : 1.25, wheel.Position.X, plotWidth);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button when button.Pressed:
                _pressed = true;
                _dragging = false;
                _pressAt = button.Position;
                _pressEnd = WindowEnd();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (_pressed && !_dragging)
                {
                    Click(button.Position, button.ShiftPressed, plotWidth);
                }

                _pressed = false;
                _dragging = false;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _pressed:
                if (!_dragging && Math.Abs(motion.Position.X - _pressAt.X) > DragThreshold)
                {
                    _dragging = true;
                    if (_live)
                    {
                        _live = false;
                        LiveChanged?.Invoke(false);
                    }
                }

                if (_dragging)
                {
                    _end = _pressEnd - (motion.Position.X - _pressAt.X) / plotWidth * _span;
                    QueueRedraw();
                }

                AcceptEvent();
                break;
        }
    }

    private void Zoom(double factor, float mouseX, float plotWidth)
    {
        double newSpan = Math.Clamp(_span * factor, MinSpan, MaxSpan);
        if (!_live && mouseX >= NameWidth)
        {
            // Keep the time under the mouse where it is.
            double atMouse = TimeAt(mouseX, plotWidth);
            _end = atMouse + (_end - atMouse) * newSpan / _span;
        }

        _span = newSpan;
        QueueRedraw();
    }

    private void Click(Vector2 at, bool shift, float plotWidth)
    {
        if (at.X < NameWidth)
        {
            int lane = (int)(at.Y / RowHeight);
            if (lane >= 0 && lane < _lanes.Length && !_unchecked.Remove(_lanes[lane]))
            {
                _unchecked.Add(_lanes[lane]);
            }
        }
        else
        {
            double time = TimeAt(at.X, plotWidth);
            if (shift)
            {
                _cursorB = time;
            }
            else
            {
                _cursorA = time;
            }
        }

        QueueRedraw();
    }
}
