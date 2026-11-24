using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Tab "Trace" of the bottom panel: a toolbar and the lanes of <see cref="TraceCanvas"/>.
/// Which tags it shows comes from the filter and search of the tag table (at most <see cref="MaxLanes"/>).
/// </summary>
public partial class TraceView : VBoxContainer
{
    public const int MaxLanes = 20;

    private readonly TagRecorder _recorder;
    private readonly TraceCanvas _canvas;
    private Button _live = null!;
    private Button _pause = null!;
    private Label _info = null!;
    private Label _status = null!;
    private int _hiddenByLimit;

    public TraceView(TagRecorder recorder)
    {
        _recorder = recorder;
        _canvas = new TraceCanvas(recorder);
    }

    /// <summary>The lanes that are drawn, checked or not, in order.</summary>
    public IReadOnlyList<string> Lanes => _canvas.Lanes;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        _live = Shell.MakeButton("Live", null, "Volgt het heden; schuiven zet Live uit");
        _live.ToggleMode = true;
        _live.ButtonPressed = true;
        _live.Toggled += on => _canvas.Live = on;
        bar.AddChild(_live);
        _pause = Shell.MakeButton("Pauzeer opname", null, "Stopt en hervat het opnemen; de simulatie loopt door");
        _pause.ToggleMode = true;
        _pause.Toggled += on => _recorder.Paused = on;
        bar.AddChild(_pause);
        bar.AddChild(Shell.MakeButton("Wis", Clear, "Wist de opname"));
        bar.AddChild(Shell.MakeButton("Exporteer trace", () => Export(), "Schrijft de wijzigingen van de aangevinkte banen als CSV naar trace.csv"));
        bar.AddChild(new VSeparator());
        _info = new Label { Modulate = Shell.Muted, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddChild(_info);
        _status = new Label { Modulate = Shell.Muted };
        bar.AddChild(_status);
        AddChild(bar);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.LiveChanged += on => _live.SetPressedNoSignal(on);
        scroll.AddChild(_canvas);
        AddChild(scroll);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        _canvas.QueueRedraw();
        _info.Text = _canvas.CursorText() + (_hiddenByLimit > 0 ? $"   ·   nog {_hiddenByLimit} tags: kies met filter of zoekveld" : string.Empty);
    }

    /// <summary>The tags that the filter and search of the tag table let through; the first <see cref="MaxLanes"/> get a lane.</summary>
    public void SetCandidates(IEnumerable<string> tags, IEnumerable<string> analogTags)
    {
        _canvas.AnalogTags.Clear();
        _canvas.AnalogTags.UnionWith(analogTags);
        string[] all = tags.Distinct().ToArray();
        _hiddenByLimit = Math.Max(0, all.Length - MaxLanes);
        _canvas.SetLanes(all.Take(MaxLanes).ToArray());
    }

    public void Clear() => _recorder.Clear();

    /// <summary>Writes the changes of the checked lanes over the whole recording to user://trace.csv. Returns the path, or null.</summary>
    public string? Export()
    {
        string path = ProjectSettings.GlobalizePath("user://trace.csv");
        try
        {
            File.WriteAllText(path, _recorder.ToCsv(_canvas.CheckedLanes, double.NegativeInfinity, double.PositiveInfinity));
            _status.Text = path;
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Export mislukt: {e.Message}";
            return null;
        }
    }
}
