using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Plc;
using FactorySim.Core.Plc.TestPlc;
using FactorySim.Core.Tags;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Simulation controls in the toolbar (run/pause/reset, operator buttons, PLC connection) and
/// the bottom panel with two views of the tags: the tag table grouped per part, with live
/// values and force, and the I/O list that shows what every PLC input and output is wired to.
/// </summary>
public partial class Hud : Node
{
    private static readonly Color OffColor = Shell.Muted;
    private static readonly Color DarkLamp = new(0.15f, 0.25f, 0.17f);

    private const int ColName = 0;
    private const int ColValue = 4;
    private const int ColForce = 5;
    private const int IoColValue = 5;

    private readonly Shell _shell;
    private readonly SimController _sim;
    private TagMap _map;
    private TagStore _tags;
    private IReadOnlyList<IoPoint> _points = Array.Empty<IoPoint>();

    private readonly Dictionary<string, TreeItem> _tagRows = new();
    private readonly Dictionary<string, TreeItem> _ioRows = new();
    private readonly Dictionary<string, int> _forces = new();

    /// <summary>Forced number per Int tag, as the store clipped it.</summary>
    private readonly Dictionary<string, int> _forcedNumbers = new();

    private Tree _tagTree = null!;
    private Tree _ioTree = null!;
    private Button _tagsTab = null!;
    private Button _ioTab = null!;
    private Button _traceTab = null!;
    private TraceView _trace = null!;
    private Button _statsTab = null!;
    private StatsView _statsView = null!;
    private Button _collapse = null!;
    private OptionButton _filter = null!;
    private LineEdit _search = null!;
    private Label _exportStatus = null!;
    private Button _itemFilterButton = null!;
    private string? _itemFilter;
    private Label _stateLabel = null!;
    private ColorRect _runLamp = null!;
    private Button _plcButton = null!;
    private PopupPanel _plcPopup = null!;
    private Label _linkLabel = null!;
    private LineEdit _hostEdit = null!;
    private SpinBox _portSpin = null!;
    private SpinBox _unitSpin = null!;
    private Button _connectButton = null!;
    private ConfirmationDialog _remoteHostDialog = null!;
    private AcceptDialog _helpDialog = null!;

    /// <summary>The window Over (plan 28).</summary>
    public AboutDialog About { get; private set; } = null!;

    /// <summary>Plan 30: the welcome window; the button "Aan de slag" opens it.</summary>
    public WelcomeDialog WelcomeWindow { get; private set; } = null!;

    /// <summary>Plan 30: the button "Aan de slag" in the window of "?" that opens the welcome window again (the toolbar is full).</summary>
    public Button WelcomeButton { get; private set; } = null!;

    /// <summary>The folders the window Over shows; set by Main.</summary>
    public System.Func<IReadOnlyList<AboutDialog.Folder>>? AboutFolders { get; set; }
    private Label _scaleLabel = null!;
    private Button _stepButton = null!;

    public Hud(Shell shell, TagMap map, TagStore tags, SimController sim)
    {
        _shell = shell;
        _map = map;
        _tags = tags;
        _sim = sim;
    }

    /// <summary>Returns the active PLC link, or null.</summary>
    public required Func<IPlcLink?> GetLink { get; init; }

    /// <summary>Name of the scene, for the comment at the top of the exported GVL.</summary>
    public Func<string> SceneName { get; init; } = () => "scene";

    /// <summary>The recording that the tab Trace shows.</summary>
    public TagRecorder Recorder { get; init; } = new();

    /// <summary>The tab Trace, for the self-test.</summary>
    public TraceView Trace => _trace;

    /// <summary>The figures that the tab Cijfers shows.</summary>
    public ProductionStats Stats { get; init; } = new();

    /// <summary>Actuator tags of the driven parts, whose on-time the tab Cijfers shows.</summary>
    public Func<IReadOnlyCollection<string>> DrivenTags { get; init; } = () => Array.Empty<string>();

    /// <summary>The tab Cijfers, for the self-test.</summary>
    public StatsView StatsView => _statsView;

    public required Action<ModbusLinkOptions> ConnectPlc { get; init; }

    public required Action DisconnectPlc { get; init; }

    /// <summary>Starts the built-in test PLC and connects to it (plan 29).</summary>
    public required Action StartTestPlc { get; init; }

    /// <summary>True while the link goes to the built-in test PLC.</summary>
    public required Func<bool> IsTestPlc { get; init; }

    /// <summary>Plan 35: the menu Help ▾ (Aan de slag, sneltoetsen, Over), for the self-test.</summary>
    public MenuButton HelpMenu { get; private set; } = null!;

    public const int HelpWelcome = 0, HelpShortcuts = 1, HelpAbout = 2;

    /// <summary>The button "Ingebouwde test-PLC", for the self-test.</summary>
    public Button TestPlcButton { get; private set; } = null!;

    /// <summary>Raised when a row of a scene item is clicked in one of the tag views, with the item id.</summary>
    public event Action<string>? PartClicked;

    /// <summary>True while the operator holds the Start button.</summary>
    public bool StartPressed { get; private set; }

    /// <summary>True while the operator holds the Stop button.</summary>
    public bool StopPressed { get; private set; }

    /// <summary>Counters and frame rate, set by the scene every frame.</summary>
    public string Info
    {
        set => _shell.StatusRight.Text = value;
    }

    /// <summary>Tag names in the tag table as it is shown now, after filter and search.</summary>
    public IReadOnlyCollection<string> VisibleTags => _tagRows.Keys;

    /// <summary>Names of the groups in the tag table, in order.</summary>
    public IReadOnlyList<string> Groups { get; private set; } = Array.Empty<string>();

    /// <summary>Rows of the I/O list: (inputs, outputs).</summary>
    public (int Inputs, int Outputs) IoRowCounts { get; private set; }

    public override void _Ready()
    {
        // The controls must keep working while the simulation is paused.
        ProcessMode = ProcessModeEnum.Always;

        BuildSimulationTools();
        BuildConnectionTools();
        BuildBottomPanel();
        RebuildTrees();

        _sim.StateChanged += _ => UpdateStateLabel();
        _sim.TimeScaleChanged += _ => UpdateStateLabel();
        UpdateStateLabel();
    }

    public override void _Process(double delta)
    {
        UpdateLinkStatus();
        UpdateApiStatus();
        UpdateClock();
        foreach (TagState tag in _tags.Snapshot())
        {
            bool analog = tag.Kind == TagKind.Int;
            bool on = analog ? tag.EffectiveNumber != 0 : tag.EffectiveValue;
            string text = (analog ? AnalogText.Format(tag.EffectiveNumber, tag.Scale, tag.Unit) : on ? "● aan" : "○ uit")
                + (tag.Forced ? "  (force)" : string.Empty);
            Color colour = tag.Forced ? Shell.Warn : on ? Shell.Good : OffColor;
            if (_tagRows.TryGetValue(tag.Name, out TreeItem? row))
            {
                row.SetText(ColValue, text);
                row.SetCustomColor(ColValue, colour);
            }

            if (_ioRows.TryGetValue(tag.Name, out TreeItem? ioRow))
            {
                ioRow.SetText(IoColValue, text);
                ioRow.SetCustomColor(IoColValue, colour);
            }

            if (tag.Name == TagNames.RunLamp)
            {
                _runLamp.Color = tag.EffectiveValue ? Shell.Good : DarkLamp;
            }
        }
    }

    /// <summary>Shows the tags of another scene: rebuilds both tag views.</summary>
    public void SetTags(TagMap map, TagStore tags, IReadOnlyList<IoPoint> points)
    {
        _map = map;
        _tags = tags;
        _points = points;
        _forces.Clear();
        _forcedNumbers.Clear();
        _itemFilter = null;
        if (IsNodeReady())
        {
            RebuildTrees();
        }

        UpdateTestPlcButton();
    }

    /// <summary>Plan 34: the test PLC only sorts the MVP scene; in any other scene its button is off, with the reason.</summary>
    private void UpdateTestPlcButton()
    {
        if (TestPlcButton is null)
        {
            return;
        }

        TestPlcFit fit = TestPlcFit.Check(_map.Entries);
        TestPlcButton.Disabled = !fit.Fits;
        TestPlcButton.TooltipText = fit.Fits
            ? "Start de test-PLC in de app en verbind ermee: sorteren op hoogte voor de MVP-scène, zonder Python of CODESYS"
            : fit.Reason;
    }

    /// <summary>The part whose tags are all that the tables show; null shows everything.</summary>
    public string? ItemFilterId => _itemFilter;

    /// <summary>
    /// Shows only the tags of one part in the tag table and the I/O list, or all of them again for null. A
    /// button next to the filter says which part it is and takes the filter away.
    /// </summary>
    public void ShowOnly(string? itemId)
    {
        _itemFilter = itemId is not null && _points.Any(point => point.ItemId == itemId) ? itemId : null;
        if (IsNodeReady())
        {
            RebuildTrees();
        }
    }

    /// <summary>Sets filter and search text of the tag table.</summary>
    public void SetFilter(TagFilter filter, string search)
    {
        _filter.Selected = (int)filter;
        _search.Text = search;
        RebuildTrees();
    }

    /// <summary>Switches the bottom panel between the tag table and the I/O list.</summary>
    public void ShowIoList(bool io) => ShowTab(io ? 1 : 0);

    /// <summary>Shows the tab Trace in the bottom panel.</summary>
    public void ShowTrace() => ShowTab(2);

    /// <summary>Shows the tab Cijfers in the bottom panel.</summary>
    public void ShowStats() => ShowTab(3);

    /// <summary>The PLC fields of the connection window: host, port and unit ID. Setting them does not connect.</summary>
    public (string Host, int Port, int UnitId) PlcFields
    {
        get => (_hostEdit.Text.Trim(), (int)_portSpin.Value, (int)_unitSpin.Value);
        set
        {
            _hostEdit.Text = value.Host;
            _portSpin.Value = value.Port;
            _unitSpin.Value = value.UnitId;
        }
    }

    /// <summary>The tab shown in the bottom panel: 0 = Tags, 1 = I/O-lijst, 2 = Trace, 3 = Cijfers.</summary>
    public int BottomTab
    {
        get => _ioTab.ButtonPressed ? 1 : _traceTab.ButtonPressed ? 2 : _statsTab.ButtonPressed ? 3 : 0;
        set
        {
            bool collapsed = BottomCollapsed;
            ShowTab(Math.Clamp(value, 0, 3));
            BottomCollapsed = collapsed;
        }
    }

    /// <summary>Whether the bottom panel is folded in; keeps the arrow on the button in step.</summary>
    public bool BottomCollapsed
    {
        get => _shell.BottomCollapsed;
        set
        {
            _shell.BottomCollapsed = value;
            _collapse.Text = value ? "▸" : "▾";
        }
    }

    /// <summary>0 = Tags, 1 = I/O-lijst, 2 = Trace, 3 = Cijfers.</summary>
    private void ShowTab(int tab)
    {
        _tagsTab.SetPressedNoSignal(tab == 0);
        _ioTab.SetPressedNoSignal(tab == 1);
        _traceTab.SetPressedNoSignal(tab == 2);
        _statsTab.SetPressedNoSignal(tab == 3);
        _tagTree.Visible = tab == 0;
        _ioTree.Visible = tab == 1;
        _trace.Visible = tab == 2;
        _statsView.Visible = tab == 3;
        _shell.BottomCollapsed = false;
        _collapse.Text = "▾";
    }

    /// <summary>Keyboard shortcut: force a tag on, or release it if it is already forced.</summary>
    public void ToggleForce(string tagName)
    {
        if (_map.Entries.All(entry => entry.Name != tagName) || IsAnalog(tagName))
        {
            return;
        }

        SetForce(tagName, _forces.GetValueOrDefault(tagName) == 0 ? 2 : 0);
    }

    /// <summary>Forces a bool tag: 0 releases it, 1 forces it off, 2 forces it on. For an Int tag 1 and 2 force 0 and 1.</summary>
    public void SetForce(string tagName, int selection)
    {
        if (IsAnalog(tagName))
        {
            SetForceNumber(tagName, selection == 0 ? null : selection - 1);
            return;
        }

        if (selection == 0)
        {
            _forces.Remove(tagName);
            _tags.Release(tagName);
        }
        else
        {
            _forces[tagName] = selection;
            _tags.Force(tagName, selection == 2);
        }

        if (_tagRows.TryGetValue(tagName, out TreeItem? row))
        {
            row.SetRange(ColForce, selection);
        }

        if ((TagFilter)_filter.Selected == TagFilter.Forced)
        {
            RebuildTrees();
        }
    }

    /// <summary>Forces an Int tag to a number (clipped to its range), or releases it for null.</summary>
    public void SetForceNumber(string tagName, int? number)
    {
        if (!IsAnalog(tagName))
        {
            return;
        }

        if (number is null)
        {
            _forces.Remove(tagName);
            _forcedNumbers.Remove(tagName);
            _tags.Release(tagName);
        }
        else
        {
            _tags.Force(tagName, number.Value);
            _forces[tagName] = 1;
            _forcedNumbers[tagName] = _tags.Snapshot().Single(tag => tag.Name == tagName).ForcedNumber;
        }

        if (_tagRows.TryGetValue(tagName, out TreeItem? row))
        {
            row.SetText(ColForce, ForceText(tagName));
        }

        if ((TagFilter)_filter.Selected == TagFilter.Forced)
        {
            RebuildTrees();
        }
    }

    /// <summary>Releases every force and resets the force selectors.</summary>
    public void ClearForces()
    {
        _tags.ReleaseAll();
        _forces.Clear();
        _forcedNumbers.Clear();
        foreach ((string name, TreeItem row) in _tagRows)
        {
            if (IsAnalog(name))
            {
                row.SetText(ColForce, string.Empty);
            }
            else
            {
                row.SetRange(ColForce, 0);
            }
        }

        if (IsNodeReady() && (TagFilter)_filter.Selected == TagFilter.Forced)
        {
            RebuildTrees();
        }
    }

    // --------------------------------------------------------------- toolbar

    private void BuildSimulationTools()
    {
        HBoxContainer bar = _shell.ToolbarSim;
        bar.AddChild(new VSeparator());
        bar.AddChild(Shell.MakeButton("▶ Run", _sim.Run, "Simulatie starten"));
        bar.AddChild(Shell.MakeButton("⏸ Pause", _sim.Pause, "Simulatie pauzeren"));
        bar.AddChild(Shell.MakeButton("⟲ Reset", _sim.Reset, "Dozen weg, alles terug in de beginstand"));
        _stateLabel = new Label();      // plan 35: as wide as "gepauzeerd", see FixWidth
        bar.AddChild(_stateLabel);
        FixWidth(_stateLabel, "draait", "gepauzeerd", "gestopt");
        bar.AddChild(Shell.MakeButton("«", _sim.Slower, "Trager (−)"));
        _scaleLabel = new Label { CustomMinimumSize = new Vector2(44.0f, 0.0f), HorizontalAlignment = HorizontalAlignment.Center };
        bar.AddChild(_scaleLabel);
        bar.AddChild(Shell.MakeButton("»", _sim.Faster, "Sneller (+)"));
        _stepButton = Shell.MakeButton("Stap", _sim.Step, "Eén tick (1/120 s) vooruit; alleen in pauze (.)");
        bar.AddChild(_stepButton);
        bar.AddChild(new VSeparator());

        Button start = Shell.MakeButton("Start", null, "Startknop van de installatie (tag Start button)");
        start.ButtonDown += () => StartPressed = true;
        start.ButtonUp += () => StartPressed = false;
        bar.AddChild(start);

        Button stop = Shell.MakeButton("Stop", null, "Stopknop van de installatie (tag Stop button)");
        stop.ButtonDown += () => StopPressed = true;
        stop.ButtonUp += () => StopPressed = false;
        bar.AddChild(stop);

        _runLamp = new ColorRect
        {
            CustomMinimumSize = new Vector2(18.0f, 18.0f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "In bedrijf (tag Run lamp)",
            Color = DarkLamp,
        };
        bar.AddChild(_runLamp);
        bar.AddChild(new VSeparator());

        // Mutes every siren in the scene; they keep flashing.
        Button mute = Shell.MakeButton("Geluid uit", null, "Alle sirenes stil; het knipperen blijft");
        mute.ToggleMode = true;
        mute.Toggled += pressed => FactorySim.App.Parts.Siren.Muted = pressed;
        bar.AddChild(mute);
    }

    private void BuildConnectionTools()
    {
        _plcButton = Shell.MakeButton("PLC: niet verbonden", TogglePlcPopup, "Verbinding met de PLC (Modbus TCP)");
        _shell.ToolbarRight.AddChild(_plcButton);
        FixWidth(_plcButton, "PLC: niet verbonden", "PLC: verbonden", "PLC: test-PLC", "PLC: geen verbinding");

        // Plan 35: one menu instead of the buttons "?" and "Over", so the toolbar fits from 1280 px.
        HelpMenu = new MenuButton { Text = "Help ▾", Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Aan de slag, sneltoetsen en Over" };
        PopupMenu help = HelpMenu.GetPopup();
        help.AddItem("Aan de slag", HelpWelcome);
        help.AddItem("Sneltoetsen en muis", HelpShortcuts);
        help.AddItem("Over FactorySim", HelpAbout);
        help.IdPressed += OnHelpItem;
        _shell.ToolbarRight.AddChild(HelpMenu);

        _plcPopup = new PopupPanel();
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(420.0f, 0.0f) };
        column.AddChild(Shell.Heading("PLC-verbinding (Modbus TCP)"));
        var row = new HBoxContainer();
        _hostEdit = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(130.0f, 0.0f), TooltipText = "Host" };
        row.AddChild(_hostEdit);
        _portSpin = new SpinBox { MinValue = 1, MaxValue = 65535, Value = 502, TooltipText = "Poort (CODESYS: 502, fake-PLC: 5020)" };
        row.AddChild(_portSpin);
        row.AddChild(new Label { Text = "Unit" });
        _unitSpin = new SpinBox { MinValue = 0, MaxValue = 255, Value = 255, TooltipText = "Unit ID (CODESYS SP16+: 0 of 255)" };
        row.AddChild(_unitSpin);
        _connectButton = Shell.MakeButton("Verbinden", OnConnectPressed);
        row.AddChild(_connectButton);
        column.AddChild(row);
        var testRow = new HBoxContainer();
        TestPlcButton = Shell.MakeButton("Ingebouwde test-PLC", StartTestPlc,
            "Start de test-PLC in de app en verbind ermee: sorteren op hoogte voor de MVP-scène, zonder Python of CODESYS");
        testRow.AddChild(TestPlcButton);
        UpdateTestPlcButton();
        testRow.AddChild(new Label { Text = "sorteert de MVP-scène; geen Python nodig", Modulate = OffColor });
        column.AddChild(testRow);
        _linkLabel = new Label
        {
            Modulate = OffColor,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400.0f, 44.0f),
        };
        column.AddChild(_linkLabel);
        BuildApiTools(column);
        _plcPopup.AddChild(column);
        AddChild(_plcPopup);

        _remoteHostDialog = new ConfirmationDialog
        {
            Title = "Verbinden met een andere computer?",
            DialogText = "Modbus TCP heeft geen authenticatie of versleuteling.\n"
                + "Deze app is bedoeld voor een SoftPLC op deze pc.\n"
                + "Verbind nooit met een PLC die echte machines aanstuurt.\n\nToch verbinden?",
            OkButtonText = "Toch verbinden",
            CancelButtonText = "Annuleren",
        };
        _remoteHostDialog.Confirmed += ConnectWithCurrentSettings;
        AddChild(_remoteHostDialog);

        _helpDialog = new AcceptDialog
        {
            Title = "Sneltoetsen en muis",
            OkButtonText = "Sluiten",
            DialogText =
                "Beeld\n"
                + "  Rechtermuisknop slepen      draaien\n"
                + "  Muiswiel                    zoomen\n"
                + "  F                           focus op de selectie\n"
                + "  Home                        hele scène in beeld\n\n"
                + "Ontwerpen\n"
                + "  Klik in de library          onderdeel kiezen, klik op de vloer om te plaatsen\n"
                + "  Klik op een onderdeel       selecteren; slepen verplaatst\n"
                + "  Band of bocht slepen        klikt vast aan het eind van een andere (groene stip)\n"
                + "  Alt+slepen                  slepen zonder vastklikken\n"
                + "  Shift+klik                  onderdeel toevoegen aan of weghalen uit de selectie\n"
                + "  Shift+slepen in lege ruimte kader trekken en alles erin selecteren\n"
                + "  Ctrl+A                      alles selecteren\n"
                + "  R                           90° draaien (een groep om zijn midden)\n"
                + "  Delete                      verwijderen\n"
                + "  Ctrl+Z / Ctrl+Y             ongedaan maken / opnieuw\n"
                + "  Ctrl+C / Ctrl+V             kopiëren / plakken bij de muis\n"
                + "  Ctrl+D                      dupliceren\n"
                + "  Esc                         plaatsen stoppen, selectie opheffen\n\n"
                + "Simuleren\n"
                + "  Klik op een knop in beeld   bedienen (ingedrukt houden waar nodig)\n"
                + "  Force in de tag-tabel       een tag vast op 0 of 1 zetten\n"
                + "  + / −                       sneller / trager (×0,25 tot ×4)\n"
                + "  .                           één tick vooruit (in pauze)",
        };
        AddChild(_helpDialog);
        WelcomeButton = _helpDialog.AddButton("Aan de slag", right: false, "welcome");
        WelcomeButton.Name = "WelcomeButton";
        WelcomeButton.TooltipText = "De drie stappen naar een draaiende sorteerlijn";
        _helpDialog.CustomAction += action =>
        {
            if (action == "welcome")
            {
                _helpDialog.Hide();
                WelcomeWindow.PopupCentered();
            }
        };

        About = new AboutDialog(() => AboutFolders?.Invoke() ?? System.Array.Empty<AboutDialog.Folder>());
        AddChild(About);

        WelcomeWindow = new WelcomeDialog();
        AddChild(WelcomeWindow);
    }

    private void TogglePlcPopup()
    {
        if (_plcPopup.Visible)
        {
            _plcPopup.Hide();
            return;
        }

        Vector2 corner = _plcButton.GetGlobalRect().End;
        _plcPopup.Popup(new Rect2I((int)corner.X - 440, (int)corner.Y + 6, 440, 270));
    }

    private void OnConnectPressed()
    {
        if (GetLink() is not null)
        {
            DisconnectPlc();
            return;
        }

        if (ModbusLinkOptions.IsLoopback(_hostEdit.Text))
        {
            ConnectWithCurrentSettings();
        }
        else
        {
            _remoteHostDialog.PopupCentered();
        }
    }

    private void ConnectWithCurrentSettings()
    {
        ConnectPlc(new ModbusLinkOptions
        {
            Host = _hostEdit.Text.Trim(),
            Port = (int)_portSpin.Value,
            UnitId = (byte)_unitSpin.Value,
        });
    }

    /// <summary>What an item of Help ▾ does; the self-test calls it with the id.</summary>
    public void OnHelpItem(long id)
    {
        switch (id)
        {
            case HelpWelcome:
                WelcomeWindow.PopupCentered();
                break;
            case HelpShortcuts:
                _helpDialog.PopupCentered();
                break;
            case HelpAbout:
                About.PopupCentered();
                break;
        }
    }

    /// <summary>
    /// Plan 35: a control whose text changes gets the width of its widest text, so the toolbar does not grow
    /// (and push the side panels out) when, say, "PLC: geen verbinding" appears.
    /// </summary>
    private static void FixWidth(Control control, params string[] texts)
    {
        string before = control is Button b ? b.Text : ((Label)control).Text;
        float widest = control.CustomMinimumSize.X;
        foreach (string text in texts)
        {
            SetText(control, text);
            widest = Mathf.Max(widest, control.GetCombinedMinimumSize().X);
        }

        SetText(control, before);
        control.CustomMinimumSize = new Vector2(Mathf.Ceil(widest), control.CustomMinimumSize.Y);
    }

    private static void SetText(Control control, string text)
    {
        if (control is Button button)
        {
            button.Text = text;
        }
        else
        {
            ((Label)control).Text = text;
        }
    }

    private void UpdateLinkStatus()
    {
        IPlcLink? link = GetLink();
        _connectButton.Text = link is null ? "Verbinden" : "Verbreken";
        if (link is null)
        {
            _plcButton.Text = "PLC: niet verbonden";
            _plcButton.Modulate = Colors.White;
            _linkLabel.Text = "Niet verbonden — actuatoren alleen via force.";
            _linkLabel.Modulate = OffColor;
            return;
        }

        LinkStatistics stats = link.Statistics;
        bool test = IsTestPlc();
        if (link.Status == LinkStatus.Connected)
        {
            _plcButton.Text = test ? "PLC: test-PLC" : "PLC: verbonden";
            _plcButton.Modulate = Shell.Good;
            _linkLabel.Text = (test ? "Verbonden met de ingebouwde test-PLC (sorteren, MVP-scène)" : "Verbonden")
                + $" · uitwisseling {stats.AverageExchangeMs:0.00} ms gem., {stats.MaxExchangeMs:0.0} ms max"
                + $" · {stats.Exchanges} uitwisselingen · {stats.Errors} fouten · {stats.Reconnects} herverbonden";
            _linkLabel.Modulate = Shell.Good;
        }
        else
        {
            _plcButton.Text = "PLC: geen verbinding";
            _plcButton.Modulate = Shell.Warn;
            _linkLabel.Text = $"Geen verbinding, opnieuw proberen… ({stats.Errors} fouten) {stats.LastError}";
            _linkLabel.Modulate = Shell.Warn;
        }
    }

    private void UpdateStateLabel()
    {
        _stateLabel.Text = _sim.State switch
        {
            SimState.Running => "draait",
            SimState.Paused => "gepauzeerd",
            _ => "gestopt",
        };
        _stateLabel.Modulate = _sim.IsRunning ? Shell.Good : OffColor;
        _shell.StatusLeft.Text = "Simulatie " + _stateLabel.Text;
        _shell.StatusLeft.Modulate = _stateLabel.Modulate;
        _scaleLabel.Text = SimController.ScaleLabel(_sim.TimeScale);
        _scaleLabel.Modulate = _sim.TimeScale == 1.0 ? Colors.White : Shell.Warn;
        _stepButton.Disabled = _sim.State != SimState.Paused;
    }

    private void UpdateClock()
    {
        _shell.StatusClock.Text = "t = " + _sim.SimTime.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("nl-NL")) + " s";
        if (_sim.TimeScale == 1.0)
        {
            _shell.StatusScale.Text = string.Empty;
            return;
        }

        string? warning = _sim.TimeScaleWarning(GetLink()?.Status == LinkStatus.Connected);
        _shell.StatusScale.Text = SimController.ScaleLabel(_sim.TimeScale) + (warning is null ? string.Empty : "  ·  " + warning);
    }

    // ---------------------------------------------------------- bottom panel

    private void BuildBottomPanel()
    {
        HBoxContainer header = _shell.BottomHeader;
        _collapse = Shell.MakeButton("▾", null, "Paneel in- of uitklappen");
        _collapse.Pressed += () =>
        {
            _shell.BottomCollapsed = !_shell.BottomCollapsed;
            _collapse.Text = _shell.BottomCollapsed ? "▸" : "▾";
        };
        header.AddChild(_collapse);

        _tagsTab = Shell.MakeButton("Tags", () => ShowIoList(false), "Alle tags per onderdeel, met waarde en force");
        _ioTab = Shell.MakeButton("I/O-lijst", () => ShowIoList(true), "Ingangen en uitgangen van de PLC en waar ze op zijn aangesloten");
        _tagsTab.ToggleMode = true;
        _traceTab = Shell.MakeButton("Trace", ShowTrace, "Tijdlijn van de tags, zoals een logic analyzer");
        _ioTab.ToggleMode = true;
        _traceTab.ToggleMode = true;
        _statsTab = Shell.MakeButton("Cijfers", ShowStats, "Productiecijfers: doorvoer, doorlooptijd en aan-tijd, in simulatietijd");
        _statsTab.ToggleMode = true;
        _tagsTab.ButtonPressed = true;
        header.AddChild(_tagsTab);
        header.AddChild(_ioTab);
        header.AddChild(_traceTab);
        header.AddChild(_statsTab);
        header.AddChild(new VSeparator());

        _filter = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Welke tags tonen" };
        foreach (string name in new[] { "Alle", "Sensoren", "Actuatoren", "Geforceerd", "Analoog" })
        {
            _filter.AddItem(name);
        }

        _filter.ItemSelected += _ => RebuildTrees();
        header.AddChild(_filter);

        _search = new LineEdit
        {
            PlaceholderText = "Zoek op naam, onderdeel, variabele of adres",
            CustomMinimumSize = new Vector2(330.0f, 0.0f),
            ClearButtonEnabled = true,
        };
        _search.TextChanged += _ => RebuildTrees();
        header.AddChild(_search);

        _itemFilterButton = Shell.MakeButton(string.Empty, () => ShowOnly(null), "Toont weer alle tags");
        _itemFilterButton.Visible = false;
        header.AddChild(_itemFilterButton);

        // Plan 35: the path of an export is long; it clips with "…" and the tooltip has it whole.
        _exportStatus = new Label
        {
            Modulate = OffColor,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        header.AddChild(_exportStatus);
        header.AddChild(Shell.MakeButton("Exporteer CSV", ExportCsv, "Schrijft de tag-mapping en de I/O-lijst als CSV-bestand"));
        header.AddChild(Shell.MakeButton("Exporteer GVL", () => ExportGvl(), "Schrijft de I/O-lijst als Global Variable List (ST) voor CODESYS"));

        _tagTree = MakeTree(new[] { "Tag", "Richting", "Modbus", "PLC-variabele", "Waarde", "Force" },
            new[] { 260, 90, 140, 260, 130, 80 });
        _tagTree.ItemEdited += OnForceEdited;
        _tagTree.ItemSelected += () => OnRowSelected(_tagTree);
        _shell.BottomBody.AddChild(_tagTree);

        _ioTree = MakeTree(new[] { "Adres", "Modbus", "PLC-variabele", "Aangesloten op", "Signaal", "Waarde" },
            new[] { 150, 150, 260, 280, 150, 130 });
        _ioTree.ItemSelected += () => OnRowSelected(_ioTree);
        _ioTree.Visible = false;
        _shell.BottomBody.AddChild(_ioTree);

        _trace = new TraceView(Recorder) { Visible = false };
        _shell.BottomBody.AddChild(_trace);

        _statsView = new StatsView(Stats, DrivenTags) { Visible = false };
        _shell.BottomBody.AddChild(_statsView);
    }

    private void SetExportStatus(string text)
    {
        _exportStatus.Text = text;
        _exportStatus.TooltipText = text;
    }

    private static Tree MakeTree(string[] titles, int[] widths)
    {
        var tree = new Tree
        {
            Columns = titles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            FocusMode = Control.FocusModeEnum.None,
            SelectMode = Tree.SelectModeEnum.Row,
        };
        for (int column = 0; column < titles.Length; column++)
        {
            tree.SetColumnTitle(column, titles[column]);
            tree.SetColumnTitleAlignment(column, HorizontalAlignment.Left);
            tree.SetColumnCustomMinimumWidth(column, widths[column]);
            tree.SetColumnExpand(column, column == 0 || column == 3);
        }

        return tree;
    }

    private void RebuildTrees()
    {
        var filter = (TagFilter)Math.Max(0, _filter.Selected);
        string search = _search.Text;
        IoPoint[] shown = IoList.ForItem(_points, _itemFilter)
            .Where(point => IoList.Matches(point, filter, search, _forces.ContainsKey(point.Tag.Name)))
            .ToArray();
        string? filteredPart = _itemFilter is null ? null : _points.FirstOrDefault(point => point.ItemId == _itemFilter)?.Part;
        _itemFilterButton.Visible = filteredPart is not null;
        _itemFilterButton.Text = filteredPart is null ? string.Empty : $"Alleen: {filteredPart}  ✕";

        // Tag table: one group per part, in scene order.
        _tagTree.Clear();
        _tagRows.Clear();
        TreeItem root = _tagTree.CreateItem();
        var groups = new List<string>();
        foreach (IGrouping<string, IoPoint> group in shown.GroupBy(point => point.ItemId ?? string.Empty))
        {
            IoPoint first = group.First();
            groups.Add(first.Part);
            TreeItem head = _tagTree.CreateItem(root);
            head.SetText(ColName, first.Part);
            head.SetText(2, group.Count() == 1 ? "1 tag" : $"{group.Count()} tags");
            head.SetCustomColor(2, OffColor);
            head.SetText(3, first.AssetName ?? "knoppen en status van de simulator");
            head.SetCustomColor(3, OffColor);
            head.SetMetadata(ColName, first.ItemId ?? string.Empty);
            head.SetSelectable(ColForce, false);
            foreach (IoPoint point in group)
            {
                TreeItem row = _tagTree.CreateItem(head);
                row.SetText(ColName, point.Tag.Name);
                row.SetTooltipText(ColName, point.Tag.Description ?? point.Tag.Name);
                row.SetText(1, point.Tag.Direction == TagDirection.Sensor ? "→ PLC" : "← PLC");
                row.SetText(2, IoList.ModbusText(point.Tag));
                row.SetText(3, point.Tag.Variable ?? string.Empty);
                if (point.Tag.IsAnalog)
                {
                    // A number field: a number forces the tag, an empty field releases it.
                    row.SetCellMode(ColForce, TreeItem.TreeCellMode.String);
                    row.SetText(ColForce, ForceText(point.Tag.Name));
                    row.SetTooltipText(ColForce, "Getal op de lijn om te forcen; leeg maken laat los");
                }
                else
                {
                    row.SetCellMode(ColForce, TreeItem.TreeCellMode.Range);
                    row.SetText(ColForce, "–,0,1");
                    row.SetRangeConfig(ColForce, 0.0, 2.0, 1.0);
                    row.SetRange(ColForce, _forces.GetValueOrDefault(point.Tag.Name));
                }

                row.SetEditable(ColForce, true);
                row.SetMetadata(ColName, point.ItemId ?? string.Empty);
                row.SetMetadata(ColForce, point.Tag.Name);
                _tagRows[point.Tag.Name] = row;
            }
        }

        Groups = groups;
        _trace.SetCandidates(shown.Select(point => point.Tag.Name),
            _points.Where(point => point.Tag.IsAnalog).Select(point => point.Tag.Name));

        // I/O list: PLC inputs, then PLC outputs, by address.
        _ioTree.Clear();
        _ioRows.Clear();
        TreeItem ioRoot = _ioTree.CreateItem();
        IoPoint[] inputs = IoList.PlcInputs(shown).ToArray();
        IoPoint[] outputs = IoList.PlcOutputs(shown).ToArray();
        AddIoGroup(ioRoot, "Ingangen van de PLC", "sensoren in de simulatie", inputs);
        AddIoGroup(ioRoot, "Uitgangen van de PLC", "actuatoren in de simulatie", outputs);
        IoRowCounts = (inputs.Length, outputs.Length);
    }

    private void AddIoGroup(TreeItem root, string title, string what, IoPoint[] points)
    {
        TreeItem head = _ioTree.CreateItem(root);
        head.SetText(0, title);
        head.SetText(1, points.Length == 1 ? "1 punt" : $"{points.Length} punten");
        head.SetCustomColor(1, OffColor);
        head.SetText(3, what);
        head.SetCustomColor(3, OffColor);
        head.SetMetadata(0, string.Empty);
        foreach (IoPoint point in points)
        {
            TreeItem row = _ioTree.CreateItem(head);
            row.SetText(0, point.Tag.IecAddress ?? string.Empty);
            row.SetText(1, IoList.ModbusText(point.Tag));
            row.SetText(2, point.Tag.Variable ?? string.Empty);
            row.SetText(3, point.AssetName is null ? point.Part : $"{point.Part}  ({point.AssetName})");
            row.SetText(4, point.Signal.Length > 0
                ? point.Signal
                : point.Tag.IsAnalog ? "getal" : point.Tag.Direction == TagDirection.Sensor ? "signaal" : "aan/uit");
            row.SetMetadata(0, point.ItemId ?? string.Empty);
            _ioRows[point.Tag.Name] = row;
        }
    }

    private void OnForceEdited()
    {
        TreeItem? row = _tagTree.GetEdited();
        if (row is null || _tagTree.GetEditedColumn() != ColForce)
        {
            return;
        }

        string tagName = row.GetMetadata(ColForce).AsString();
        if (IsAnalog(tagName))
        {
            string text = row.GetText(ColForce).Trim();
            if (text.Length == 0 || text == "–" || text == "-")
            {
                SetForceNumber(tagName, null);
            }
            else if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
            {
                SetForceNumber(tagName, number);
            }
            else
            {
                row.SetText(ColForce, ForceText(tagName));
            }

            return;
        }

        SetForce(tagName, (int)row.GetRange(ColForce));
    }

    private bool IsAnalog(string tagName) => _map.Entries.Any(entry => entry.Name == tagName && entry.IsAnalog);

    /// <summary>Text of the force field of an Int tag: the forced number, or empty.</summary>
    private string ForceText(string tagName) =>
        _forcedNumbers.TryGetValue(tagName, out int number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private void OnRowSelected(Tree tree)
    {
        string itemId = tree.GetSelected()?.GetMetadata(0).AsString() ?? string.Empty;
        if (itemId.Length > 0)
        {
            PartClicked?.Invoke(itemId);
        }
    }

    /// <summary>Writes the I/O list as a GVL in Structured Text to user://IO.gvl.st. Returns the path, or null when it failed.</summary>
    public string? ExportGvl()
    {
        string path = ProjectSettings.GlobalizePath("user://IO.gvl.st");
        try
        {
            File.WriteAllText(path, GvlExport.ToSt(_points, SceneName()));
            SetExportStatus(path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetExportStatus($"Export mislukt: {e.Message}");
            return null;
        }
    }

    private void ExportCsv()
    {
        string tagMap = ProjectSettings.GlobalizePath("user://tagmap.csv");
        string ioList = ProjectSettings.GlobalizePath("user://iolist.csv");
        try
        {
            File.WriteAllText(tagMap, _map.ToCsv());
            File.WriteAllText(ioList, IoList.ToCsv(_points));
            SetExportStatus($"{tagMap} en iolist.csv");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetExportStatus($"Export mislukt: {e.Message}");
        }
    }
}
