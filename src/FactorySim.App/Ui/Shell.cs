using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// The window layout: a toolbar on top, the library on the left, the properties on the right,
/// the tag panels at the bottom and a status bar. The 3D view fills the window behind it; the
/// part that the panels leave free is <see cref="FreeRect"/>. This class only makes the
/// frames; the HUD and the scene editor fill them.
/// </summary>
public partial class Shell : CanvasLayer
{
    public static readonly Color Muted = new(0.62f, 0.65f, 0.70f);
    public static readonly Color Good = new(0.35f, 0.95f, 0.45f);
    public static readonly Color Warn = new(1.0f, 0.75f, 0.2f);
    public static readonly Color Accent = new(0.36f, 0.62f, 0.98f);

    private const float SideWidth = 310.0f;
    private const float BottomHeight = 215.0f;

    private VBoxContainer _root = null!;
    private PanelContainer _leftPanel = null!;
    private PanelContainer _rightPanel = null!;
    private Control _bottomBody = null!;
    private Control _viewArea = null!;

    /// <summary>Toolbar sections, left to right: mode switch, scene and edit tools, simulation, and the right-hand end.</summary>
    public HBoxContainer ToolbarMode { get; private set; } = null!;

    public HBoxContainer ToolbarEdit { get; private set; } = null!;

    public HBoxContainer ToolbarSim { get; private set; } = null!;

    public HBoxContainer ToolbarRight { get; private set; } = null!;

    /// <summary>The whole toolbar row; its minimum width must fit the window (plan 35).</summary>
    public HBoxContainer Toolbar { get; private set; } = null!;

    public VBoxContainer Left { get; private set; } = null!;

    public VBoxContainer Right { get; private set; } = null!;

    /// <summary>Row above the bottom panel: tabs, filter, search.</summary>
    public HBoxContainer BottomHeader { get; private set; } = null!;

    /// <summary>Content of the bottom panel; children fill it.</summary>
    public Control BottomBody => _bottomBody;

    /// <summary>Buttons floating in the top right corner of the 3D view.</summary>
    public VBoxContainer ViewButtons { get; private set; } = null!;

    public Label StatusLeft { get; private set; } = null!;

    /// <summary>Simulation time.</summary>
    public Label StatusClock { get; private set; } = null!;

    /// <summary>Time scale when it is not 1, with the warning about an external PLC.</summary>
    public Label StatusScale { get; private set; } = null!;

    public Label StatusMessage { get; private set; } = null!;

    /// <summary>Plan 31: "Details" next to an error in the status line; opens the log. Hidden until an error comes.</summary>
    public Button StatusDetails { get; private set; } = null!;

    public Label StatusWarning { get; private set; } = null!;

    public Label StatusRight { get; private set; } = null!;

    /// <summary>The part of the window not covered by panels, in pixels.</summary>
    public Rect2 FreeRect => _viewArea.GetGlobalRect();

    /// <summary>Plan 35: where the library and the properties panel are drawn, for the self-test.</summary>
    public Rect2 LeftPanelRect => _leftPanel.GetGlobalRect();

    public Rect2 RightPanelRect => _rightPanel.GetGlobalRect();

    /// <summary>Plan 35: the width of the whole layout; wider than the window when the toolbar does not fit.</summary>
    public float LayoutWidth => _root.Size.X;

    public bool BottomCollapsed
    {
        get => !_bottomBody.Visible;
        set => _bottomBody.Visible = !value;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        var root = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = BuildTheme() };
        _root = root;
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        var toolbar = new HBoxContainer();
        Toolbar = toolbar;
        toolbar.AddThemeConstantOverride("separation", 8);       // plan 35: 10 did not fit at 1280 px
        ToolbarMode = Section(toolbar);
        toolbar.AddChild(new VSeparator());
        ToolbarEdit = Section(toolbar);
        ToolbarSim = Section(toolbar);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        ToolbarRight = Section(toolbar);
        root.AddChild(Panel(toolbar, 8));

        var middle = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        middle.AddThemeConstantOverride("separation", 0);
        root.AddChild(middle);

        Left = new VBoxContainer { CustomMinimumSize = new Vector2(SideWidth, 0.0f) };
        _leftPanel = Panel(Left, 10);
        middle.AddChild(_leftPanel);

        _viewArea = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        middle.AddChild(_viewArea);
        ViewButtons = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        ViewButtons.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        ViewButtons.GrowHorizontal = Control.GrowDirection.Begin;
        ViewButtons.Position = new Vector2(-10.0f, 10.0f);
        _viewArea.AddChild(ViewButtons);

        Right = new VBoxContainer { CustomMinimumSize = new Vector2(SideWidth, 0.0f) };
        _rightPanel = Panel(Right, 10);
        middle.AddChild(_rightPanel);

        var bottom = new VBoxContainer();
        BottomHeader = new HBoxContainer();
        BottomHeader.AddThemeConstantOverride("separation", 8);
        bottom.AddChild(BottomHeader);
        _bottomBody = new MarginContainer { CustomMinimumSize = new Vector2(0.0f, BottomHeight) };
        bottom.AddChild(_bottomBody);
        root.AddChild(Panel(bottom, 8));

        var status = new HBoxContainer();
        status.AddThemeConstantOverride("separation", 18);
        StatusLeft = new Label();
        StatusClock = new Label { Modulate = Muted };
        StatusScale = new Label { Modulate = Warn };
        StatusMessage = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        StatusWarning = new Label { Modulate = Warn };
        StatusRight = new Label { Modulate = Muted };
        status.AddChild(StatusLeft);
        status.AddChild(StatusClock);
        status.AddChild(StatusScale);
        status.AddChild(StatusMessage);
        StatusDetails = MakeButton("Details", null, "Open het log van de app");
        StatusDetails.Name = "StatusDetails";
        StatusDetails.Visible = false;
        status.AddChild(StatusDetails);
        status.AddChild(StatusWarning);
        status.AddChild(StatusRight);
        root.AddChild(Panel(status, 5));
    }

    /// <summary>Shows or hides the library and the properties (design mode only).</summary>
    public void ShowSidePanels(bool visible)
    {
        _leftPanel.Visible = visible;
        _rightPanel.Visible = visible;
    }

    /// <summary>A toolbar or panel button; it never takes keyboard focus, so shortcuts keep working.</summary>
    public static Button MakeButton(string text, System.Action? onPressed, string? tooltip = null)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip ?? string.Empty };
        if (onPressed is not null)
        {
            button.Pressed += onPressed;
        }

        return button;
    }

    public static Label Heading(string text) => new() { Text = text, Modulate = Muted };

    private static HBoxContainer Section(HBoxContainer toolbar)
    {
        var section = new HBoxContainer();
        section.AddThemeConstantOverride("separation", 6);
        toolbar.AddChild(section);
        return section;
    }

    private static PanelContainer Panel(Control content, int margin)
    {
        var panel = new PanelContainer();
        var box = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            box.AddThemeConstantOverride(side, margin);
        }

        box.AddChild(content);
        panel.AddChild(box);
        return panel;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 14 };
        theme.SetStylebox("panel", "PanelContainer", new StyleBoxFlat
        {
            BgColor = new Color(0.105f, 0.12f, 0.145f, 0.97f),
            BorderColor = new Color(0.22f, 0.25f, 0.30f),
            BorderWidthBottom = 1,
            BorderWidthRight = 1,
        });
        theme.SetStylebox("normal", "Button", ButtonBox(new Color(0.18f, 0.21f, 0.25f)));
        theme.SetStylebox("hover", "Button", ButtonBox(new Color(0.24f, 0.28f, 0.34f)));
        theme.SetStylebox("pressed", "Button", ButtonBox(new Color(0.20f, 0.38f, 0.66f)));
        theme.SetStylebox("disabled", "Button", ButtonBox(new Color(0.14f, 0.16f, 0.19f)));
        theme.SetConstant("separation", "VBoxContainer", 6);
        return theme;
    }

    private static StyleBoxFlat ButtonBox(Color colour)
    {
        var box = new StyleBoxFlat { BgColor = colour };
        box.SetCornerRadiusAll(5);
        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 10;
        box.ContentMarginTop = 4;
        box.ContentMarginBottom = 4;
        return box;
    }
}
