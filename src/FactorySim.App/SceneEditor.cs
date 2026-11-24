using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.App.Ui;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Scene design mode: pick an asset from the library, place it on the floor, move, rotate,
/// rename or delete it, and save, open, export or import the scene as a file.
/// The simulation is stopped while designing; "Simuleren" rebuilds the scene and its tags.
/// </summary>
public partial class SceneEditor : CanvasLayer
{
    public const string FileExtension = ScenePaths.Extension;
    private const string UserScenesFolder = ScenePaths.OwnFolder;

    private static readonly Color Warning = new(1.0f, 0.75f, 0.2f);

    private readonly Main _main;
    private readonly List<CatalogAsset> _paletteAssets = new();

    private readonly SceneHistory _history = new();
    private Shell _shell = null!;
    private Button _designButton = null!;
    private Button _simulateButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private LineEdit _sceneName = null!;
    private LineEdit _paletteSearch = null!;
    private string? _categoryId;
    private ItemList _palette = null!;
    private Label _assetInfo = null!;
    private VBoxContainer _selectionBox = null!;
    private Label _noSelection = null!;
    private LineEdit _itemName = null!;
    private Label _itemInfo = null!;
    private SpinBox _itemX = null!;
    private SpinBox _itemZ = null!;
    private SpinBox _itemYaw = null!;
    private VBoxContainer _paramBox = null!;
    private Label _itemTags = null!;
    private readonly List<SceneItem> _clipboard = new();
    private readonly List<string> _selection = new();
    private readonly List<MeshInstance3D> _groupMarks = new();
    private readonly Dictionary<string, Vector3> _dragOrigins = new();
    private readonly Dictionary<string, float> _dragYaws = new();
    private readonly List<(MeshInstance3D Mark, Vector3 Origin, float Yaw)> _dragMarks = new();
    private PortSnap? _dragSnap;
    private PortSnap? _placeSnap;
    private MeshInstance3D _snapDot = null!;
    private Node3D _overlapRoot = null!;
    private Node3D _flashRoot = null!;
    private StandardMaterial3D? _flashMaterial;
    private double _flashLeft;
    private MeshInstance3D _marquee = null!;
    private Vector3? _marqueeStart;
    private string? _dragPrimary;
    private Vector2? _dragDelta;
    private ConfirmationDialog _floorDialog = null!;
    private ConfirmationDialog _simTagsDialog = null!;
    private readonly Dictionary<string, CheckBox> _simTagChecks = new();
    private SpinBox _floorLength = null!;
    private SpinBox _floorWidth = null!;
    private Vector3? _lastFloorPoint;
    private FileDialog _fileDialog = null!;
    private Action<string>? _onFileChosen;

    private Node3D? _ghost;
    private CatalogAsset? _placing;
    private float _placingYaw;
    private MeshInstance3D _highlight = null!;
    private SpinBox _itemHeight = null!;
    private bool _updatingSelectionUi;
    private string? _selectedId;
    private bool _dragging;

    public SceneEditor(Main main)
    {
        _main = main;
    }

    public bool DesignMode { get; private set; }

    public string? SelectedId => _selectedId;

    /// <summary>Everything that is selected; the last one is <see cref="SelectedId"/>.</summary>
    public IReadOnlyList<string> SelectedIds => _selection;

    /// <summary>How many orange markers show items that stand through each other (only in design mode).</summary>
    public int OverlapMarkerCount => _overlapRoot.GetChildCount();

    /// <summary>The part that is lighting up after a click on its tags, or null.</summary>
    public string? FlashId { get; private set; }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>Assets shown in the library panel now, after category and search.</summary>
    public IReadOnlyList<CatalogAsset> PaletteAssets => _paletteAssets;

    /// <summary>Names of the parameter fields shown for the selected item.</summary>
    public IReadOnlyList<string> ParamFieldNames { get; private set; } = Array.Empty<string>();

    /// <summary>Ids in the list *Item* of the selected emitter; the first, empty, is "standaard".</summary>
    public IReadOnlyList<string> EmitChoiceIds { get; private set; } = Array.Empty<string>();

    /// <summary>The item the selected emitter was set to; null for the default of its asset.</summary>
    public string? SelectedEmits => _selectedId is null ? null : _main.Scene.Find(_selectedId)?.Emits;

    /// <summary>The text of the selected sign, or null.</summary>
    public string? SelectedText => _selectedId is null ? null : _main.Scene.Find(_selectedId)?.Text;

    /// <summary>Text of the overlap warning in the status bar; empty when nothing overlaps.</summary>
    public string OverlapWarning { get; private set; } = string.Empty;

    /// <summary>Warning about sensors that need a reflector or an emitter in their line of sight and have none.</summary>
    public string SensorWarning { get; private set; } = string.Empty;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // The selection marker must exist before the panel is built: the panel updates it.
        _highlight = new MeshInstance3D
        {
            Name = "SelectionHighlight",
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = Shapes.Unshaded(new Color(1.0f, 0.8f, 0.1f, 0.35f)),
            Visible = false,
        };
        _main.AddChild(_highlight);
        _overlapRoot = new Node3D { Name = "OverlapMarkers" };
        _main.AddChild(_overlapRoot);
        _flashRoot = new Node3D { Name = "FlashMarker" };
        _main.AddChild(_flashRoot);
        _marquee = new MeshInstance3D
        {
            Name = "SelectionFrame",
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = Shapes.Unshaded(new Color(0.3f, 0.6f, 1.0f, 0.3f)),
            Visible = false,
        };
        _main.AddChild(_marquee);
        _snapDot = new MeshInstance3D
        {
            Name = "SnapDot",
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f },
            MaterialOverride = Shapes.Unshaded(new Color(0.2f, 0.95f, 0.3f)),
            Visible = false,
        };
        _main.AddChild(_snapDot);

        _shell = _main.Shell;
        BuildToolbar();
        BuildLibraryPanel();
        BuildPropertiesPanel();
        BuildFileDialog();
        BuildFloorDialog();
        BuildSimTagsDialog();
        BuildGalleryDialog();
        BuildBlockDialogs();
        BuildAutosave();
        _main.SceneChanged += OnSceneChanged;
        _shell.ShowSidePanels(false);
        _shell.ToolbarEdit.Visible = false;
        _shell.ToolbarSim.Visible = true;
        UpdateSelectionUi();
    }

    // ------------------------------------------------------------ operations
    // The buttons and the mouse use these; so does the self-test.

    public void SetDesignMode(bool design)
    {
        if (design == DesignMode)
        {
            return;
        }

        if (!design)
        {
            // Leaving design mode: the scene must be able to run.
            string? error = _main.ApplyScene(_main.Scene);
            if (error is not null)
            {
                ShowStatus(error, isError: true);
                UpdateModeButtons();
                return;
            }

            AutosaveIfChanged();                // plan 16: also on leaving design mode
        }
        else
        {
            _main.Sim.Reset();
        }

        DesignMode = design;
        CancelPlacing();
        Select(null);
        _shell.ShowSidePanels(design);
        _shell.ToolbarEdit.Visible = design;
        _shell.ToolbarSim.Visible = !design;      // plan 35: Run, Pause, … only while simulating
        UpdateModeButtons();
        UpdateOverlapWarning();
        if (design)
        {
            _sceneName.Text = _main.Scene.Name;
            ShowStatus("Kies een onderdeel in de library en klik op de vloer om het te plaatsen.");
        }
        else
        {
            ShowStatus("Simuleren: druk op Run. Zonder PLC stuur je actuatoren met Force in de tag-tabel.");
        }
    }

    public string? NewScene()
    {
        Select(null);
        _history.Clear();
        string? error = _main.ApplyScene(new Scene());
        if (error is null)
        {
            _main.NoteScenePath(null);
            MarkSaved();
        }

        _sceneName.Text = _main.Scene.Name;
        UpdateHistoryButtons();
        return Report(error, "Nieuwe, lege scène.");
    }

    /// <summary>Places an asset at a floor position and selects it. Returns null when it cannot be placed.</summary>
    public SceneItem? Place(string assetId, float x, float z, float yaw = 0.0f, PortSnap? clickedOn = null)
    {
        CatalogAsset? asset = _main.Catalog.Find(assetId);
        if (asset is null || !asset.IsPlaceable)
        {
            ShowStatus($"'{assetId}' kan niet geplaatst worden.", isError: true);
            return null;
        }

        _history.Record(_main.Scene);
        SceneItem item = _main.Scene.Add(asset, x, z, yaw);
        if (clickedOn is not null)
        {
            item = _main.Scene.Place(item.Id, clickedOn.X, clickedOn.Z, clickedOn.Yaw);   // plan 17: same undo step
        }

        if (Rebuild() is not null)
        {
            _main.Scene.Remove(item.Id);
            _history.DropLast();
            Rebuild();
            return null;
        }

        Select(item.Id);
        ShowStatus(asset.IsFunctional
            ? $"{item.Name} geplaatst."
            : $"{item.Name} geplaatst — alleen visueel, heeft geen gedrag in de simulatie.");
        return item;
    }

    public void Select(string? itemId)
    {
        _selection.Clear();
        _selectedId = itemId is not null && _main.Scene.Find(itemId) is not null ? itemId : null;
        if (_selectedId is not null)
        {
            _selection.Add(_selectedId);
        }

        UpdateSelectionUi();
    }

    /// <summary>Shift-click: adds the item to the selection, or takes it out when it is in already.</summary>
    public void ToggleSelect(string itemId)
    {
        if (_main.Scene.Find(itemId) is null)
        {
            return;
        }

        if (!_selection.Remove(itemId))
        {
            _selection.Add(itemId);
        }

        _selectedId = _selection.Count > 0 ? _selection[^1] : null;
        UpdateSelectionUi();
    }

    /// <summary>Makes exactly these items the selection; ids that are not in the scene are skipped.</summary>
    public void SelectMany(IEnumerable<string> itemIds)
    {
        _selection.Clear();
        foreach (string id in itemIds)
        {
            if (_main.Scene.Find(id) is not null && !_selection.Contains(id))
            {
                _selection.Add(id);
            }
        }

        _selectedId = _selection.Count > 0 ? _selection[^1] : null;
        UpdateSelectionUi();
    }

    public void SelectAll() => SelectMany(_main.Scene.Items.Select(item => item.Id).ToArray());

    /// <summary>The item under a point on the screen, found through the floor like the clicks in design mode; null when none.</summary>
    public SceneItem? ItemAtScreen(Vector2 mouse) =>
        FloorPoint(mouse) is { } point ? ScenePicking.ItemAt(_main.Scene, _main.Catalog, point.X, point.Z) : null;

    /// <summary>Lights the part up with a cyan, see-through box for a few seconds, so that it is easy to find, also while simulating.</summary>
    public void FlashItem(string itemId)
    {
        ClearFlash();
        if (_main.Scene.Find(itemId) is not { } item || _main.Catalog.Find(item.Asset) is not { } asset)
        {
            return;
        }

        var size = new Vector3((float)(asset.BoundsMax[0] - asset.BoundsMin[0]), (float)(asset.BoundsMax[1] - asset.BoundsMin[1]),
            (float)(asset.BoundsMax[2] - asset.BoundsMin[2]));
        var middle = new Vector3((float)(asset.BoundsMin[0] + asset.BoundsMax[0]) / 2.0f, (float)(asset.BoundsMin[1] + asset.BoundsMax[1]) / 2.0f,
            (float)(asset.BoundsMin[2] + asset.BoundsMax[2]) / 2.0f);
        _flashMaterial = Shapes.Unshaded(new Color(0.2f, 0.9f, 1.0f, 0.4f));
        var holder = new Node3D { Position = new Vector3((float)item.X, (float)item.Y, (float)item.Z), RotationDegrees = new Vector3(0.0f, (float)item.Yaw, 0.0f) };
        holder.AddChild(Shapes.BoxMesh(size + new Vector3(0.1f, 0.1f, 0.1f), _flashMaterial, middle));
        _flashRoot.AddChild(holder);
        FlashId = itemId;
        _flashLeft = 3.0;
    }

    public override void _Process(double delta)
    {
        PollAutosave();
        if (_flashLeft <= 0.0)
        {
            return;
        }

        _flashLeft -= delta;
        if (_flashMaterial is not null)
        {
            Color colour = _flashMaterial.AlbedoColor;
            colour.A = 0.25f + 0.2f * (float)Math.Sin(_flashLeft * 8.0);
            _flashMaterial.AlbedoColor = colour;
        }

        if (_flashLeft <= 0.0)
        {
            ClearFlash();
        }
    }

    private void ClearFlash()
    {
        foreach (Node child in _flashRoot.GetChildren())
        {
            _flashRoot.RemoveChild(child);
            child.QueueFree();
        }

        _flashLeft = 0.0;
        FlashId = null;
        _flashMaterial = null;
    }

    /// <summary>Moves everything that is selected over the floor by the same amount. One step for undo.</summary>
    public void MoveSelectedBy(float dx, float dz)
    {
        if (_selection.Count == 0 || (Math.Abs(dx) < 0.0005f && Math.Abs(dz) < 0.0005f))
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.MoveMany(_selection.ToArray(), dx, dz);
        Rebuild();
    }

    public void MoveSelected(float x, float z)
    {
        if (_selectedId is null)
        {
            return;
        }

        SceneItem item = _main.Scene.Find(_selectedId)!;
        if (Scene.Snap(x) == item.X && Scene.Snap(z) == item.Z)
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.Move(_selectedId, x, z);
        Rebuild();
    }

    /// <summary>Turns the selected item to an absolute angle, in degrees.</summary>
    public void SetSelectedYaw(float degrees)
    {
        if (_selectedId is null)
        {
            return;
        }

        double current = _main.Scene.Find(_selectedId)!.Yaw;
        if (Math.Abs(current - degrees) < 0.01)
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.Rotate(_selectedId, degrees - current);
        Rebuild();
    }

    /// <summary>Copies the selected item next to itself and selects the copy.</summary>
    public SceneItem? DuplicateSelected()
    {
        if (_selectedId is null)
        {
            return null;
        }

        _history.Record(_main.Scene);
        if (_selection.Count > 1)
        {
            return FinishCopyMany(_main.Scene.DuplicateMany(_selection.ToArray(), 0.5, 0.5));
        }

        SceneItem copy = _main.Scene.Duplicate(_selectedId, 0.5, 0.5);
        return FinishCopy(copy);
    }

    /// <summary>Remembers the selected item for <see cref="PasteAt"/>.</summary>
    public void CopySelected()
    {
        _clipboard.Clear();
        _clipboard.AddRange(_selection.Select(id => _main.Scene.Find(id)).OfType<SceneItem>());
        if (_clipboard.Count > 0)
        {
            ShowStatus(_clipboard.Count == 1
                ? $"{_clipboard[0].Name} gekopieerd. Ctrl+V plakt bij de muis."
                : $"{_clipboard.Count} onderdelen gekopieerd. Ctrl+V plakt ze bij de muis.");
        }
    }

    /// <summary>Places a copy of the copied item at a floor position and selects it.</summary>
    public SceneItem? PasteAt(float x, float z)
    {
        if (_clipboard.Count == 0)
        {
            return null;
        }

        _history.Record(_main.Scene);
        return _clipboard.Count == 1
            ? FinishCopy(_main.Scene.Paste(_clipboard[0], x, z))
            : FinishCopyMany(_main.Scene.PasteMany(_clipboard.ToArray(), x, z));
    }

    /// <summary>Takes back the last change to the scene.</summary>
    public bool Undo() => Restore(_history.Undo(_main.Scene), "Ongedaan gemaakt.");

    /// <summary>Does the change that was undone last again.</summary>
    public bool Redo() => Restore(_history.Redo(_main.Scene), "Opnieuw gedaan.");

    /// <summary>Filters the library panel: a category id (null for all) and a search text.</summary>
    public void SetPaletteFilter(string? categoryId, string search)
    {
        _categoryId = categoryId;
        _paletteSearch.Text = search;
        FillPalette();
    }

    private SceneItem? FinishCopy(SceneItem copy)
    {
        if (Rebuild() is not null)
        {
            _main.Scene.Remove(copy.Id);
            _history.DropLast();
            Rebuild();
            return null;
        }

        Select(copy.Id);
        ShowStatus($"{copy.Name} toegevoegd als kopie.");
        return copy;
    }

    /// <summary>Makes the copies of a group the selection; takes them back when the scene cannot run with them.</summary>
    private SceneItem? FinishCopyMany(IReadOnlyList<SceneItem> copies)
    {
        if (Rebuild() is not null)
        {
            _main.Scene.RemoveMany(copies.Select(copy => copy.Id).ToArray());
            _history.DropLast();
            Rebuild();
            return null;
        }

        SelectMany(copies.Select(copy => copy.Id).ToArray());
        ShowStatus($"{copies.Count} onderdelen toegevoegd als kopie.");
        return copies.Count > 0 ? copies[0] : null;
    }

    private bool Restore(Scene? scene, string message)
    {
        if (scene is null)
        {
            return false;
        }

        string[] selected = _selection.ToArray();
        string? error = _main.ApplyScene(scene);
        _sceneName.Text = _main.Scene.Name;
        SelectMany(selected);
        Report(error, message);
        UpdateHistoryButtons();
        return error is null;
    }

    /// <summary>Puts the selected item at another height above the floor, e.g. a sensor on a low roller line.</summary>
    public void SetSelectedHeight(float y)
    {
        if (_selectedId is null)
        {
            return;
        }

        if (Math.Abs(_main.Scene.Find(_selectedId)!.Y - y) < 0.0005)
        {
            return;
        }

        _history.Record(_main.Scene);
        try
        {
            _main.Scene.SetHeight(_selectedId, Math.Round(y, 3));
        }
        catch (SceneException e)
        {
            _history.DropLast();
            ShowStatus(e.Message, isError: true);
            return;
        }

        Rebuild();
    }

    /// <summary>Sets the item the selected emitter puts on the line; null or empty goes back to the default of its asset.</summary>
    public void SetSelectedEmits(string? itemId)
    {
        if (_selectedId is null)
        {
            return;
        }

        string? value = string.IsNullOrEmpty(itemId) ? null : itemId;
        if (_main.Scene.Find(_selectedId)!.Emits == value)
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.SetEmits(_selectedId, value);
        Rebuild();
    }

    /// <summary>Sets the text of the selected sign (plan 19): one step for undo, nothing when it does not change.</summary>
    public void SetSelectedText(string? text)
    {
        if (_selectedId is not null)
        {
            SetItemText(_selectedId, text);
        }
    }

    /// <summary>Sets the text of a sign by id: the text field may lose focus after another item was clicked.</summary>
    private void SetItemText(string itemId, string? text)
    {
        if (_main.Scene.Find(itemId) is not { } item || item.Text == SignText.Normalize(text))
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.SetText(itemId, text);
        Rebuild();
    }

    /// <summary>Overrides a parameter of the selected item, e.g. "highRatio" of an emitter.</summary>
    public void SetSelectedParam(string name, double value)
    {
        if (_selectedId is null)
        {
            return;
        }

        _history.Record(_main.Scene);
        _main.Scene.SetParam(_selectedId, name, value);
        Rebuild();
    }

    public void RotateSelected(float degrees = 90.0f)
    {
        if (_selectedId is null)
        {
            return;
        }

        _history.Record(_main.Scene);
        if (_selection.Count > 1)
        {
            // A group turns about its own middle.
            (double cx, double cz) = _main.Scene.Centre(_selection.ToArray());
            _main.Scene.RotateMany(_selection.ToArray(), degrees, cx, cz);
        }
        else
        {
            _main.Scene.Rotate(_selectedId, degrees);
        }

        Rebuild();
    }

    public void DeleteSelected()
    {
        if (_selectedId is null)
        {
            return;
        }

        string name = _selection.Count > 1 ? $"{_selection.Count} onderdelen" : _main.Scene.Find(_selectedId)?.Name ?? _selectedId;
        _history.Record(_main.Scene);
        _main.Scene.RemoveMany(_selection.ToArray());
        _selection.Clear();
        _selectedId = null;
        Rebuild();
        ShowStatus($"{name} verwijderd.");
    }

    public string? RenameSelected(string name)
    {
        if (_selectedId is null)
        {
            return null;
        }

        string previous = _main.Scene.Find(_selectedId)!.Name;
        if (previous == name.Trim())
        {
            return null;
        }

        _history.Record(_main.Scene);
        try
        {
            _main.Scene.Rename(_selectedId, name);
        }
        catch (SceneException e)
        {
            _history.DropLast();
            ShowStatus(e.Message, isError: true);
            return e.Message;
        }

        string? error = Rebuild();
        if (error is not null)
        {
            _main.Scene.Rename(_selectedId, previous);      // e.g. the new name clashed with a tag
            _history.DropLast();
            Rebuild();
            ShowStatus(error, isError: true);
        }

        return error;
    }

    /// <summary>Saves or exports the scene. user:// for the own scenes folder, any path for export.</summary>
    public string? SaveTo(string path)
    {
        path = ScenePaths.WithSceneExtension(path);     // plan 22: an old .fslevel.json is written back as .fsscene.json
        _main.Scene.Name = string.IsNullOrWhiteSpace(_sceneName.Text) ? _main.Scene.Name : _sceneName.Text.Trim();
        string before = AutosaveScenePath;
        string? error = _main.SaveSceneFile(path);
        if (error is null)
        {
            _main.NoteScenePath(path);
            ClearAutosaves(before, path);       // plan 16: after a save the copies of this scene go
            MarkSaved();
        }

        return Report(error, $"Opgeslagen: {Display(path)}");
    }

    /// <summary>Opens or imports a scene file.</summary>
    public string? LoadFrom(string path)
    {
        Select(null);
        string? error = _main.LoadSceneFile(path);
        if (error is null)
        {
            _main.NoteScenePath(path);
            _history.Clear();
            UpdateHistoryButtons();
            _sceneName.Text = _main.Scene.Name;
            MarkSaved();
            int skipped = _main.View.Skipped.Count;
            ShowStatus(skipped == 0
                ? $"Geladen: {Display(path)}"
                : $"Geladen met {skipped} onbekend(e) onderdeel/onderdelen overgeslagen: {string.Join(", ", _main.View.Skipped)}",
                isError: skipped > 0);
        }
        else
        {
            ShowStatus($"Laden mislukt: {error}", isError: true);
        }

        if (error is null)
        {
            OfferAutosave();
        }

        return error;
    }

    /// <summary>File name in the own scenes folder for the current scene name.</summary>
    public string DefaultSavePath()
    {
        string name = string.IsNullOrWhiteSpace(_sceneName.Text) ? _main.Scene.Name : _sceneName.Text.Trim();
        return $"{UserScenesFolder}/{AutosavePolicy.SafeName(name)}{FileExtension}";
    }

    // ----------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!DesignMode)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion:
                OnMouseMoved(motion.Position);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    OnLeftPressed(click.Position, click.ShiftPressed);
                }
                else
                {
                    OnLeftReleased(click.Position);
                }

                break;

            case InputEventKey { Pressed: true, Echo: false } key when key.IsCommandOrControlPressed():
                OnControlKey(key.Keycode, key.ShiftPressed);
                break;

            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key.PhysicalKeycode);
                break;
        }
    }

    private void OnMouseMoved(Vector2 mouse)
    {
        if (FloorPoint(mouse) is not { } point)
        {
            return;
        }

        _lastFloorPoint = point;
        if (MoveBlockGhost(point))
        {
            return;
        }

        if (_ghost is not null && _placing is not null)
        {
            _placeSnap = ClickingOn() ? ScenePorts.SnapNew(_main.Scene, _main.Catalog, _placing, _placingYaw, point.X, point.Z) : null;
            _ghost.Position = _placeSnap is { } snap
                ? new Vector3((float)snap.X, (float)_placing.OriginHeight, (float)snap.Z)
                : new Vector3((float)Scene.Snap(point.X), (float)_placing.OriginHeight, (float)Scene.Snap(point.Z));
            _ghost.RotationDegrees = new Vector3(0.0f, (float)(_placeSnap?.Yaw ?? _placingYaw), 0.0f);
            ShowSnapDot(_placeSnap);
        }
        else if (_marqueeStart is { } start)
        {
            // The frame being drawn with Shift held down.
            float x0 = Mathf.Min(start.X, point.X);
            float x1 = Mathf.Max(start.X, point.X);
            float z0 = Mathf.Min(start.Z, point.Z);
            float z1 = Mathf.Max(start.Z, point.Z);
            ((PlaneMesh)_marquee.Mesh).Size = new Vector2(Mathf.Max(x1 - x0, 0.01f), Mathf.Max(z1 - z0, 0.01f));
            _marquee.Position = new Vector3((x0 + x1) / 2.0f, 0.02f, (z0 + z1) / 2.0f);
            _marquee.Visible = true;
        }
        else if (_dragging && _dragPrimary is not null && _main.Scene.Find(_dragPrimary) is { } primary)
        {
            // Move the nodes directly while dragging, all by the same amount; the scene follows on release.
            // Plan 17: a belt or curve near the free end of another clicks onto it, unless Alt is held.
            _dragSnap = ClickingOn()
                ? ScenePorts.Snap(_main.Scene, _main.Catalog, _selection.Append(primary.Id), primary.Id, point.X, point.Z)
                : null;
            float turn = (float)(_dragSnap?.TurnDegrees ?? 0.0);
            float dx = (float)((_dragSnap?.X ?? Scene.Snap(point.X)) - primary.X);
            float dz = (float)((_dragSnap?.Z ?? Scene.Snap(point.Z)) - primary.Z);
            var pivot = new Vector3((float)primary.X, 0.0f, (float)primary.Z);
            foreach ((string id, Vector3 origin) in _dragOrigins)
            {
                if (_main.View.NodeOf(id) is { } node)
                {
                    node.Position = Turned(origin, pivot, turn) + new Vector3(dx, 0.0f, dz);
                    node.RotationDegrees = new Vector3(0.0f, _dragYaws[id] + turn, 0.0f);
                }
            }

            foreach ((MeshInstance3D mark, Vector3 origin, float yaw) in _dragMarks)
            {
                mark.Position = Turned(origin, pivot, turn) + new Vector3(dx, 0.0f, dz);
                mark.RotationDegrees = new Vector3(0.0f, yaw + turn, 0.0f);
            }

            _dragDelta = new Vector2(dx, dz);
            ShowSnapDot(_dragSnap);
        }
    }


    private void OnControlKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Z when shift:
            case Key.Y:
                Redo();
                break;
            case Key.Z:
                Undo();
                break;
            case Key.A:
                SelectAll();
                break;
            case Key.D:
                DuplicateSelected();
                break;
            case Key.C:
                CopySelected();
                break;
            case Key.V when _lastFloorPoint is { } point:
                PasteAt(point.X, point.Z);
                break;
        }
    }

    private void OnLeftPressed(Vector2 mouse, bool shift)
    {
        // A text field keeps the keyboard until something else is clicked: give it back to the view.
        GetViewport().GuiReleaseFocus();
        if (FloorPoint(mouse) is not { } point)
        {
            return;
        }

        if (ClickBlock(point))
        {
            return;
        }

        if (_placing is not null)
        {
            if (!_main.Scene.OnFloor(point.X, point.Z))
            {
                ShowStatus("Dat is buiten de vloer. Kies een plek op de vloer.", isError: true);
                return;
            }

            Place(_placing.Id, point.X, point.Z, _placingYaw, _placeSnap);
            return;
        }

        SceneItem? hit = ScenePicking.ItemAt(_main.Scene, _main.Catalog, point.X, point.Z);
        _dragDelta = null;
        if (shift)
        {
            // Shift adds an item to the selection or takes it out; in empty space it starts a frame.
            if (hit is not null)
            {
                ToggleSelect(hit.Id);
            }
            else
            {
                _marqueeStart = point;
            }

            _dragging = false;
            return;
        }

        if (hit is null || !_selection.Contains(hit.Id))
        {
            Select(hit?.Id);
        }

        _dragging = hit is not null;
        _dragPrimary = hit?.Id;
        _dragOrigins.Clear();
        _dragYaws.Clear();
        _dragMarks.Clear();
        _dragSnap = null;
        if (_dragging)
        {
            foreach (string id in _selection)
            {
                if (_main.View.NodeOf(id) is { } node)
                {
                    _dragOrigins[id] = node.Position;
                    _dragYaws[id] = node.RotationDegrees.Y;
                }
            }

            foreach (MeshInstance3D mark in _groupMarks.Prepend(_highlight).Where(mark => mark.Visible))
            {
                _dragMarks.Add((mark, mark.Position, mark.RotationDegrees.Y));
            }
        }
    }

    private void OnLeftReleased(Vector2 mouse)
    {
        if (_marqueeStart is { } start)
        {
            _marqueeStart = null;
            _marquee.Visible = false;
            if (FloorPoint(mouse) is { } end)
            {
                var framed = ScenePicking.ItemsIn(_main.Scene, _main.Catalog, Math.Min(start.X, end.X), Math.Min(start.Z, end.Z),
                    Math.Max(start.X, end.X), Math.Max(start.Z, end.Z)).Select(item => item.Id);
                SelectMany(_selection.Concat(framed).Distinct().ToArray());
            }
        }

        if (_dragging && _dragSnap is { } snap && _dragPrimary is not null)
        {
            ApplySnap(_dragPrimary, snap);
        }
        else if (_dragging && _dragDelta is { } delta)
        {
            MoveSelectedBy(delta.X, delta.Y);
        }

        _dragging = false;
        _dragDelta = null;
        _dragPrimary = null;
        _dragSnap = null;
        _dragOrigins.Clear();
        _dragYaws.Clear();
        _dragMarks.Clear();
        ShowSnapDot(null);
    }

    private void OnKey(Key key)
    {
        switch (key)
        {
            case Key.R when _placingBlock is not null:
                TurnBlockGhost();
                break;
            case Key.R when _placing is not null:
                _placingYaw = (_placingYaw + 90.0f) % 360.0f;
                if (_ghost is not null)
                {
                    _ghost.RotationDegrees = new Vector3(0.0f, _placingYaw, 0.0f);
                }

                break;
            case Key.R:
                RotateSelected();
                break;
            case Key.Delete:
            case Key.Backspace:
                DeleteSelected();
                break;
            case Key.Escape:
                CancelPlacing();
                _marqueeStart = null;
                _marquee.Visible = false;
                Select(null);
                break;
        }
    }

    /// <summary>Where the mouse ray meets the floor, or null when it points at the sky.</summary>
    private Vector3? FloorPoint(Vector2 mouse)
    {
        Camera3D camera = _main.Camera;
        Vector3 origin = camera.ProjectRayOrigin(mouse);
        Vector3 direction = camera.ProjectRayNormal(mouse);
        if (direction.Y >= -0.001f)
        {
            return null;
        }

        return origin + direction * (-origin.Y / direction.Y);
    }

    // -------------------------------------------------------------------- UI

    private void BuildToolbar()
    {
        _designButton = Shell.MakeButton("✎ Ontwerpen", () => SetDesignMode(true), "De scène bouwen en aanpassen");
        _simulateButton = Shell.MakeButton("▶ Simuleren", () => SetDesignMode(false), "De scène laten draaien");
        _designButton.ToggleMode = true;
        _simulateButton.ToggleMode = true;
        _shell.ToolbarMode.AddChild(_designButton);
        _shell.ToolbarMode.AddChild(_simulateButton);
        UpdateModeButtons();

        HBoxContainer bar = _shell.ToolbarEdit;
        var menu = new MenuButton { Text = "Scène ▾", Flat = false, FocusMode = Control.FocusModeEnum.None };
        PopupMenu popup = menu.GetPopup();
        string[] entries = { "Nieuw", "Opslaan", "Openen…", "Exporteren…", "Importeren…", "Vloer…", "Simulatie-tags…", "Beschrijving…" };
        for (int i = 0; i < entries.Length; i++)
        {
            popup.AddItem(entries[i], i);
        }

        popup.IdPressed += id =>
        {
            switch (id)
            {
                case 0:
                    NewScene();
                    break;
                case 1:
                    SaveTo(DefaultSavePath());
                    break;
                case 2:
                    ShowGallery();
                    break;
                case 3:
                    AskFile(save: true, own: false, path => SaveTo(path));
                    break;
                case 4:
                    AskFile(save: false, own: false, path => LoadFrom(path));
                    break;
                case 5:
                    AskFloor();
                    break;
                case 6:
                    AskSimTags();
                    break;
                case 7:
                    AskDescription();
                    break;
            }
        };
        bar.AddChild(menu);

        _sceneName = new LineEdit { CustomMinimumSize = new Vector2(190.0f, 0.0f), TooltipText = "Naam van de scène" };
        _sceneName.TextChanged += text => _main.Scene.Name = text;
        bar.AddChild(_sceneName);

        _undoButton = Shell.MakeButton("↶", () => Undo(), "Ongedaan maken (Ctrl+Z)");
        _redoButton = Shell.MakeButton("↷", () => Redo(), "Opnieuw (Ctrl+Y)");
        bar.AddChild(_undoButton);
        bar.AddChild(_redoButton);
        UpdateHistoryButtons();
    }

    private void BuildLibraryPanel()
    {
        VBoxContainer column = _shell.Left;
        column.AddChild(Shell.Heading("Library"));
        _paletteSearch = new LineEdit { PlaceholderText = "Zoek onderdeel", ClearButtonEnabled = true };
        _paletteSearch.TextChanged += _ => FillPalette();
        column.AddChild(_paletteSearch);

        var categories = new HFlowContainer();
        var group = new ButtonGroup();
        AddCategoryButton(categories, group, "Alle", null);
        foreach (CatalogCategory category in _main.Catalog.Categories)
        {
            if (_main.Catalog.Placeable(category.Id).Any())
            {
                AddCategoryButton(categories, group, category.Name, category.Id);
            }
        }

        AddCategoryButton(categories, group, "Bouwblokken", BlocksCategory);

        column.AddChild(categories);

        _palette = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(72, 72),
            FixedColumnWidth = 84,
            MaxColumns = 0,
            SameColumnWidth = true,
            MaxTextLines = 2,
            FocusMode = Control.FocusModeEnum.None,
        };
        _palette.ItemSelected += index =>
        {
            if (_categoryId == BlocksCategory)
            {
                StartPlacingBlock((int)index);
            }
            else
            {
                StartPlacing(_paletteAssets[(int)index]);
            }
        };
        column.AddChild(_palette);
        BuildBlockActions(column);
        _assetInfo = new Label
        {
            Modulate = Shell.Muted,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0.0f, 66.0f),
        };
        column.AddChild(_assetInfo);
        FillPalette();
    }

    private void AddCategoryButton(Container parent, ButtonGroup group, string text, string? categoryId)
    {
        Button button = Shell.MakeButton(text, null);
        button.ToggleMode = true;
        button.ButtonGroup = group;
        button.ButtonPressed = categoryId == _categoryId;
        button.Pressed += () =>
        {
            _categoryId = categoryId;
            FillPalette();
        };
        parent.AddChild(button);
    }

    private void BuildPropertiesPanel()
    {
        VBoxContainer column = _shell.Right;
        column.AddChild(Shell.Heading("Eigenschappen"));
        _noSelection = new Label
        {
            Text = "Niets geselecteerd.\nKlik op een onderdeel in de scène of op een rij in de tag-tabel.",
            Modulate = Shell.Muted,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_noSelection);
        BuildSaveBlockButton(column);

        _selectionBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _itemName = new LineEdit { TooltipText = "Naam; de tags van het onderdeel heten hiernaar. Enter past toe." };
        _itemName.TextSubmitted += text => RenameSelected(text);
        _selectionBox.AddChild(_itemName);
        _itemInfo = new Label { Modulate = Shell.Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _selectionBox.AddChild(_itemInfo);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        _itemX = AddNumber(grid, "X (m)", -7.0, 7.0, Scene.GridStep, value => MoveSelected((float)value, (float)_itemZ.Value));
        _itemZ = AddNumber(grid, "Z (m)", -5.0, 5.0, Scene.GridStep, value => MoveSelected((float)_itemX.Value, (float)value));
        _itemHeight = AddNumber(grid, "Hoogte (m)", 0.0, 5.0, 0.05, value => SetSelectedHeight((float)value));
        _itemYaw = AddNumber(grid, "Draaiing (°)", 0.0, 345.0, 15.0, value => SetSelectedYaw((float)value));
        _selectionBox.AddChild(grid);

        _paramBox = new VBoxContainer();
        _selectionBox.AddChild(_paramBox);

        var actionRow = new HBoxContainer();
        actionRow.AddChild(Shell.MakeButton("Draai 90°", () => RotateSelected(), "R"));
        actionRow.AddChild(Shell.MakeButton("Dupliceer", () => DuplicateSelected(), "Ctrl+D"));
        actionRow.AddChild(Shell.MakeButton("Verwijder", DeleteSelected, "Delete"));
        _selectionBox.AddChild(actionRow);

        _selectionBox.AddChild(new HSeparator());
        _selectionBox.AddChild(Shell.Heading("Aansluitingen"));
        _itemTags = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _selectionBox.AddChild(_itemTags);
        column.AddChild(_selectionBox);
    }

    private SpinBox AddNumber(Container parent, string label, double min, double max, double step, Action<double> changed)
    {
        parent.AddChild(new Label { Text = label });
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(120.0f, 0.0f) };
        box.ValueChanged += value =>
        {
            if (!_updatingSelectionUi)
            {
                changed(value);
            }
        };
        parent.AddChild(box);
        return box;
    }

    /// <summary>One row per parameter the selected asset lets the user change.</summary>
    private void FillParams(SceneItem item, CatalogAsset asset)
    {
        foreach (Node child in _paramBox.GetChildren())
        {
            _paramBox.RemoveChild(child);
            child.QueueFree();
        }

        IReadOnlyList<ParamField> fields = ItemParams.Editable(asset);
        ParamFieldNames = fields.Select(field => field.Name).ToArray();
        EmitChoiceIds = Array.Empty<string>();
        if (fields.Count == 0)
        {
            return;
        }

        _paramBox.AddChild(new HSeparator());
        _paramBox.AddChild(Shell.Heading("Instellingen"));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        if (asset.Behaviour == AssetBehaviours.Emitter)
        {
            grid.AddChild(new Label { Text = "Item", TooltipText = "Wat deze emitter op de lijn zet", MouseFilter = Control.MouseFilterEnum.Stop });
            grid.AddChild(BuildItemChoice(item, asset));
        }

        if (asset.Behaviour == AssetBehaviours.Sign)
        {
            grid.AddChild(new Label { Text = "Tekst", TooltipText = TextTooltip, MouseFilter = Control.MouseFilterEnum.Stop });
            grid.AddChild(BuildTextField(item));
        }

        foreach (ParamField field in fields)
        {
            string name = field.Name;
            double value = item.Param(name, asset, field.Default);
            (string label, string tooltip) = asset.Behaviour == AssetBehaviours.Robot ? RobotParamText(name) : ParamText(name);
            grid.AddChild(new Label { Text = label, TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Stop });
            if (name == SignText.SizeParam && asset.Behaviour == AssetBehaviours.Sign)
            {
                var sizes = new OptionButton { TooltipText = tooltip, CustomMinimumSize = new Vector2(120.0f, 0.0f) };
                sizes.AddItem("Klein");
                sizes.AddItem("Middel");
                sizes.AddItem("Groot");
                sizes.Select((int)SignText.SizeOf(value));
                sizes.ItemSelected += index =>
                {
                    if (!_updatingSelectionUi)
                    {
                        SetSelectedParam(name, index);
                    }
                };
                grid.AddChild(sizes);
            }
            else if (field.IsToggle)
            {
                var check = new CheckBox { ButtonPressed = value > 0.5, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip };

                // A belt that always runs has no tags, so a second direction means nothing (plan 12).
                check.Disabled = name == SceneTags.TwoWay && item.Param(SceneTags.AlwaysOn, asset, 0.0) > 0.5;
                check.Toggled += on => SetSelectedParam(name, on ? 1.0 : 0.0);
                grid.AddChild(check);
            }
            else
            {
                var box = new SpinBox
                {
                    MinValue = field.Min,
                    MaxValue = field.Max,
                    Step = field.Step,
                    Value = value,
                    CustomMinimumSize = new Vector2(120.0f, 0.0f),
                    TooltipText = tooltip,
                };
                box.ValueChanged += changed => SetSelectedParam(name, changed);
                grid.AddChild(box);
            }
        }

        _paramBox.AddChild(grid);
        if (asset.Behaviour == AssetBehaviours.Robot)
        {
            FillRobotPanel(item, FactorySim.Core.Robot.RobotGeometry.FromParams((key, fallback) => item.Param(key, asset, fallback)));
        }
    }

    private static (string Label, string Tooltip) RobotParamText(string name) => name switch
    {
        "speed" => ("Snelheid (%)", "Percentage van de topsnelheid van de assen; 40 % is rustig genoeg om te volgen"),
        "pedestal" => ("Sokkel (m)", "Hoogte van de sokkel onder de voet; 0 = op de vloer"),
        _ => ParamText(name),
    };

    private static readonly string TextTooltip =
        $"Tekst op het bordje: hoogstens {SignText.MaxLength} tekens en {SignText.MaxLines} regels. Leeg toont de naam. Wordt toegepast als je het veld verlaat";

    /// <summary>The text of a sign: several lines, applied when the field loses focus (one step for undo).</summary>
    private TextEdit BuildTextField(SceneItem item)
    {
        var field = new TextEdit
        {
            Text = item.Text ?? string.Empty,
            PlaceholderText = item.Name,
            CustomMinimumSize = new Vector2(160.0f, 72.0f),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            TooltipText = TextTooltip,
        };
        string itemId = item.Id;
        field.FocusExited += () =>
        {
            // Leaving the field can happen while the panel is rebuilt: apply after this frame, never in the middle of it.
            string text = field.Text;
            if (!_updatingSelectionUi)
            {
                Callable.From(() => SetItemText(itemId, text)).CallDeferred();
            }
        };
        return field;
    }

    /// <summary>The list *Item* of an emitter: what the asset says, and every item that fits through the portal.</summary>
    private OptionButton BuildItemChoice(SceneItem item, CatalogAsset asset)
    {
        IReadOnlyList<CatalogAsset> items = EmitterItems.Choices(asset, _main.Catalog);
        string standard = asset.Emits is { } own && _main.Catalog.Find(own) is { } ownAsset ? $"Standaard ({ownAsset.Name})" : "Standaard (twee dozen)";
        EmitChoiceIds = new[] { string.Empty }.Concat(items.Select(choice => choice.Id)).ToArray();

        var list = new OptionButton { CustomMinimumSize = new Vector2(160.0f, 0.0f), TooltipText = "Alleen items die door het portaal passen" };
        list.AddItem(standard);
        foreach (CatalogAsset choice in items)
        {
            list.AddItem(choice.Name);
        }

        int selected = item.Emits is null ? 0 : Array.IndexOf(EmitChoiceIds.ToArray(), item.Emits);
        list.Select(Math.Max(selected, 0));
        list.ItemSelected += index =>
        {
            if (!_updatingSelectionUi)
            {
                SetSelectedEmits(index == 0 ? null : EmitChoiceIds[(int)index]);
            }
        };
        return list;
    }

    private static (string Label, string Tooltip) ParamText(string name) => name switch
    {
        "speed" => ("Snelheid (m/s)", "Snelheid van de band, rollen of beweging"),
        "rollSpeed" => ("Rollen (m/s)", "Snelheid van de rollen"),
        "feedSpeed" => ("Aanvoer (m/s)", "Snelheid van de aanvoerbaan"),
        "pushSpeed" => ("Duwer (m/s)", "Snelheid van de duwer"),
        "tableSpeed" => ("Heftafel (m/s)", "Snelheid van de heftafel"),
        "leafSpeed" => ("Laagplaat (m/s)", "Snelheid waarmee de laagplaat opent"),
        "speedZ" => ("Z-as (m/s)", "Snelheid van de verticale as"),
        "turnSpeed" => ("Draaien (°/s)", "Draaisnelheid"),
        "swingSpeed" => ("Zwenken (°/s)", "Zwenksnelheid van de arm"),
        "deckSpeed" => ("In/uitvoer (m/s)", "Snelheid van de invoer- en uitvoerbaan"),
        "driveSpeed" => ("Rijden (m/s)", "Snelheid van de kraan langs de gang"),
        "hoistSpeed" => ("Heffen (m/s)", "Snelheid van het hefplatform"),
        "forkSpeed" => ("Vorken (m/s)", "Snelheid waarmee de vorken uitschuiven"),
        "jog" => ("Jog-bediening", "Aan: de PLC stuurt links, rechts, omhoog en omlaag en stopt op de positiesensoren. Uit: de PLC geeft een doelvak en go"),
        "size" => ("Grootte", "Grootte van de letters"),
        "machineTime" => ("Bewerken (s)", "Hoe lang de machine met een onderdeel bezig is"),
        "start" => ("Startstand", "Stand van de knop bij het openen en na een reset, 0-1000"),
        "decimals" => ("Decimalen", "Aantal cijfers achter de komma: bij 2 toont 1234 als 12,34"),
        "maxInflow" => ("Vuldebiet (m³/s)", "Toevoer met de vulklep helemaal open"),
        "maxOutflow" => ("Afvoerdebiet (m³/s)", "Afvoer van een volle tank met de afvoerklep helemaal open; minder bij een lager niveau"),
        "interval" => ("Interval (s)", "Tijd tussen twee items"),
        "highRatio" => ("Aandeel hoog", "0 = alleen lage dozen, 1 = alleen hoge dozen"),
        "seed" => ("Reeks", "Ander getal geeft een andere volgorde van lage en hoge dozen"),
        "range" => ("Bereik (m)", "Hoe ver de sensor kijkt"),
        "post" => ("Op paal", "Uit: zonder paal, bijvoorbeeld op een kast of bordes"),
        "alwaysOn" => ("Altijd aan", "Loopt zonder PLC en krijgt dan geen tag"),
        "twoWay" => ("Twee richtingen", "Krijgt een tweede tag '<naam> reverse' om achteruit te lopen. Beide aan: de band staat stil. Doet niets bij Altijd aan"),
        "bistable" => ("Bistabiel ventiel", "Een spoel voor uit (de tag van de pusher) en een voor in ('<naam> retract'); het ventiel onthoudt de laatste stand"),
        "pulseDistance" => ("Pulsafstand (m)", "Afstand waarover de band beweegt voor één volledige periode van A en B"),
        "reverse" => ("Omlaag", "Aan: de band loopt omlaag in plaats van omhoog. Draai de band zo dat het hoge eind bij het begin ligt"),
        _ => (name, name),
    };

    private void BuildFileDialog()
    {
        _fileDialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { $"*{FileExtension}, *{ScenePaths.LegacyExtension} ; FactorySim-scène" },
            Size = new Vector2I(820, 520),
        };
        _fileDialog.FileSelected += path =>
        {
            Action<string>? action = _onFileChosen;
            _onFileChosen = null;
            action?.Invoke(path);
        };
        AddChild(_fileDialog);
    }

    private void BuildFloorDialog()
    {
        _floorDialog = new ConfirmationDialog { Title = "Vloer", OkButtonText = "Toepassen", CancelButtonText = "Annuleren", Size = new Vector2I(340, 150) };
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddChild(new Label { Text = "Lengte (m, langs X)" });
        _floorLength = new SpinBox { MinValue = Scene.MinFloor, MaxValue = Scene.MaxFloor, Step = Scene.FloorStep, CustomMinimumSize = new Vector2(110.0f, 0.0f) };
        grid.AddChild(_floorLength);
        grid.AddChild(new Label { Text = "Breedte (m, langs Z)" });
        _floorWidth = new SpinBox { MinValue = Scene.MinFloor, MaxValue = Scene.MaxFloor, Step = Scene.FloorStep, CustomMinimumSize = new Vector2(110.0f, 0.0f) };
        grid.AddChild(_floorWidth);
        _floorDialog.AddChild(grid);
        _floorDialog.Confirmed += () => SetFloor(_floorLength.Value, _floorWidth.Value);
        AddChild(_floorDialog);
    }

    private void AskFloor()
    {
        _floorLength.Value = _main.Scene.Floor.Length;
        _floorWidth.Value = _main.Scene.Floor.Width;
        _floorDialog.PopupCentered();
    }

    /// <summary>Makes the floor of the scene this size (rounded to half a metre). One step for undo. Returns an error text, or null.</summary>
    public string? SetFloor(double length, double width)
    {
        FloorSize before = _main.Scene.Floor;
        _history.Record(_main.Scene);
        try
        {
            _main.Scene.SetFloor(length, width);
        }
        catch (SceneException e)
        {
            _history.DropLast();
            ShowStatus(e.Message, isError: true);
            return e.Message;
        }

        if (_main.Scene.Floor == before)
        {
            _history.DropLast();
            return null;
        }

        string? error = Rebuild();
        ShowStatus($"Vloer: {_main.Scene.Floor.Length:0.#} × {_main.Scene.Floor.Width:0.#} m.");
        return error;
    }

    private void BuildSimTagsDialog()
    {
        _simTagsDialog = new ConfirmationDialog
        {
            Title = "Simulatie-tags", OkButtonText = "Toepassen", CancelButtonText = "Annuleren", Size = new Vector2I(420, 190),
        };
        var column = new VBoxContainer();
        column.AddChild(new Label { Text = "Extra tags naar de PLC (standaard uit):", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        (string Name, string Tip)[] options =
        {
            (SceneTags.SimPaused, "Hoog zolang de simulatie gepauzeerd is"),
            (SceneTags.SimReset, "Hoog gedurende 1 s (echte tijd) na elke reset"),
        };
        foreach ((string name, string tip) in options)
        {
            var check = new CheckBox { Text = $"{name} — {tip}" };
            _simTagChecks[name] = check;
            column.AddChild(check);
        }

        _simTagsDialog.AddChild(column);
        _simTagsDialog.Confirmed += () => SetSimTags(_simTagChecks.Where(pair => pair.Value.ButtonPressed).Select(pair => pair.Key).ToArray());
        AddChild(_simTagsDialog);
    }

    private void AskSimTags()
    {
        foreach ((string name, CheckBox check) in _simTagChecks)
        {
            check.ButtonPressed = _main.Scene.SimTags.Contains(name);
        }

        _simTagsDialog.PopupCentered();
    }

    /// <summary>
    /// Switches on exactly these optional simulation tags (plan 14) and builds the tags again.
    /// One step for undo. Returns an error text, or null.
    /// </summary>
    public string? SetSimTags(IReadOnlyList<string> names)
    {
        string[] before = _main.Scene.SimTags.ToArray();
        _history.Record(_main.Scene);
        try
        {
            _main.Scene.SetSimTags(names);
        }
        catch (SceneException e)
        {
            _history.DropLast();
            ShowStatus(e.Message, isError: true);
            return e.Message;
        }

        if (_main.Scene.SimTags.SequenceEqual(before))
        {
            _history.DropLast();
            return null;
        }

        string? error = Rebuild();
        ShowStatus(_main.Scene.SimTags.Count == 0
            ? "Simulatie-tags: uit."
            : $"Simulatie-tags: {string.Join(", ", _main.Scene.SimTags)}.");
        return error;
    }

    private void AskFile(bool save, bool own, Action<string> action)
    {
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(UserScenesFolder));
        _onFileChosen = action;
        _fileDialog.FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile;
        _fileDialog.Title = (save, own) switch
        {
            (true, _) => "Scène exporteren",
            (false, true) => "Scène openen",
            _ => "Scène importeren",
        };
        _fileDialog.CurrentDir = own
            ? ProjectSettings.GlobalizePath(UserScenesFolder)
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        if (save)
        {
            _fileDialog.CurrentFile = System.IO.Path.GetFileName(DefaultSavePath());
        }

        _fileDialog.PopupCentered();
    }

    private void FillPalette()
    {
        _palette.Clear();
        _paletteAssets.Clear();
        if (_categoryId == BlocksCategory)
        {
            FillBlockPalette();
            return;
        }

        _paletteBlocks.Clear();
        _blockActions.Visible = false;
        foreach (CatalogAsset asset in _main.Catalog.Search(_paletteSearch.Text, _categoryId))
        {
            Texture2D? icon = ResourceLoader.Exists("res://" + asset.Thumbnail) ? GD.Load<Texture2D>("res://" + asset.Thumbnail) : null;
            int index = _palette.AddItem(asset.Name, icon);
            _palette.SetItemTooltip(index, asset.IsFunctional
                ? $"{asset.Name} — werkt in de simulatie"
                : $"{asset.Name} — alleen visueel, geen gedrag");
            if (!asset.IsFunctional)
            {
                _palette.SetItemCustomFgColor(index, Shell.Muted);
            }

            _paletteAssets.Add(asset);
        }

        _assetInfo.Text = _paletteAssets.Count == 0 ? "Geen onderdelen gevonden." : "Grijze namen zijn alleen visueel.";
    }

    private void StartPlacing(CatalogAsset asset)
    {
        CancelPlacing();
        Select(null);
        _placing = asset;
        _placingYaw = 0.0f;
        _ghost = Models.Instantiate(asset.Id);
        _ghost.Name = "PlacingGhost";
        _ghost.Position = new Vector3(0.0f, (float)asset.OriginHeight, 0.0f);
        MakeTranslucent(_ghost);
        _main.AddChild(_ghost);
        _assetInfo.Text = $"{asset.Name} · {asset.Size[0]:0.##} × {asset.Size[2]:0.##} × {asset.Size[1]:0.##} m"
            + (asset.IsFunctional ? " · werkt in de simulatie" : " · alleen visueel, geen gedrag");
        ShowStatus($"Klik op de vloer om '{asset.Name}' te plaatsen. R draait, Esc stopt.");
    }

    private void CancelPlacing(bool keepPaletteChoice = false)
    {
        _ghost?.QueueFree();
        _ghost = null;
        _placing = null;
        _placingBlock = null;
        _placeSnap = null;
        _snapDot.Visible = false;
        if (!keepPaletteChoice)
        {
            _palette?.DeselectAll();
        }
    }

    /// <summary>Draws a model half see-through, so it reads as "not placed yet".</summary>
    private static void MakeTranslucent(Node node)
    {
        if (node is GeometryInstance3D geometry)
        {
            geometry.Transparency = 0.45f;
        }

        foreach (Node child in node.GetChildren())
        {
            MakeTranslucent(child);
        }
    }

    private void UpdateModeButtons()
    {
        _designButton.SetPressedNoSignal(DesignMode);
        _simulateButton.SetPressedNoSignal(!DesignMode);
    }

    private void UpdateHistoryButtons()
    {
        _undoButton.Disabled = !_history.CanUndo;
        _redoButton.Disabled = !_history.CanRedo;
    }

    private void UpdateOverlapWarning()
    {
        IReadOnlyList<(SceneItem First, SceneItem Second)> pairs = SceneOverlap.Find(_main.Scene, _main.Catalog);
        OverlapWarning = pairs.Count switch
        {
            0 => string.Empty,
            1 => $"⚠ {pairs[0].First.Name} en {pairs[0].Second.Name} staan door elkaar",
            _ => $"⚠ {pairs.Count} keer staan onderdelen door elkaar, o.a. {pairs[0].First.Name} en {pairs[0].Second.Name}",
        };
        IReadOnlyList<SceneItem> unpaired = SensorPairing.Unpaired(_main.Scene, _main.Catalog);
        SensorWarning = unpaired.Count switch
        {
            0 => string.Empty,
            1 => $"⚠ {unpaired[0].Name} ziet zijn reflector of zender niet",
            _ => $"⚠ {unpaired.Count} sensoren zien hun reflector of zender niet, o.a. {unpaired[0].Name}",
        };
        string both = OverlapWarning.Length > 0 && SensorWarning.Length > 0 ? "   " : string.Empty;
        _shell.StatusWarning.Text = DesignMode ? OverlapWarning + both + SensorWarning : string.Empty;
        UpdateOverlapMarkers();
    }

    /// <summary>An orange, see-through box round every item that stands through another one. Not in simulate mode.</summary>
    private void UpdateOverlapMarkers()
    {
        foreach (Node child in _overlapRoot.GetChildren())
        {
            _overlapRoot.RemoveChild(child);
            child.QueueFree();
        }

        if (!DesignMode)
        {
            return;
        }

        foreach (SceneItem item in SceneOverlap.Involved(_main.Scene, _main.Catalog))
        {
            if (_main.Catalog.Find(item.Asset) is not { } asset)
            {
                continue;
            }

            var size = new Vector3((float)(asset.BoundsMax[0] - asset.BoundsMin[0]), (float)(asset.BoundsMax[1] - asset.BoundsMin[1]),
                (float)(asset.BoundsMax[2] - asset.BoundsMin[2]));
            var middle = new Vector3((float)(asset.BoundsMin[0] + asset.BoundsMax[0]) / 2.0f, (float)(asset.BoundsMin[1] + asset.BoundsMax[1]) / 2.0f,
                (float)(asset.BoundsMin[2] + asset.BoundsMax[2]) / 2.0f);
            var holder = new Node3D { Name = "Overlap_" + item.Id, Position = new Vector3((float)item.X, (float)item.Y, (float)item.Z),
                RotationDegrees = new Vector3(0.0f, (float)item.Yaw, 0.0f) };
            holder.AddChild(Shapes.BoxMesh(size, Shapes.Unshaded(new Color(1.0f, 0.45f, 0.1f, 0.35f)), middle));
            _overlapRoot.AddChild(holder);
        }
    }

    private void OnSceneChanged()
    {
        if (FlashId is not null && _main.Scene.Find(FlashId) is null)
        {
            ClearFlash();
        }

        _selection.RemoveAll(id => _main.Scene.Find(id) is null);
        if (_selectedId is not null && _main.Scene.Find(_selectedId) is null)
        {
            _selectedId = _selection.Count > 0 ? _selection[^1] : null;
        }

        if (IsNodeReady())
        {
            UpdateSelectionUi();
            UpdateOverlapWarning();
            UpdateHistoryButtons();
        }
    }

    private void UpdateSelectionUi()
    {
        SceneItem? item = _selectedId is null ? null : _main.Scene.Find(_selectedId);
        CatalogAsset? asset = item is null ? null : _main.Catalog.Find(item.Asset);
        bool group = _selection.Count > 1;
        _saveBlockButton.Visible = group && DesignMode;
        _selectionBox.Visible = !group && item is not null && asset is not null;
        _noSelection.Visible = !_selectionBox.Visible;
        _highlight.Visible = !group && item is not null && asset is not null && DesignMode;
        UpdateGroupMarks(group);
        if (group)
        {
            ParamFieldNames = Array.Empty<string>();
            EmitChoiceIds = Array.Empty<string>();
            _noSelection.Text = $"{_selection.Count} onderdelen geselecteerd.\nSleep om ze samen te verplaatsen, R draait ze om hun midden, Ctrl+D dupliceert, Ctrl+C kopieert en Delete verwijdert.";
            return;
        }

        _noSelection.Text = "Niets geselecteerd.\nKlik op een onderdeel in de scène of op een rij in de tag-tabel. Shift-klik of een kader met Shift kiest er meer.";
        if (item is null || asset is null)
        {
            ParamFieldNames = Array.Empty<string>();
            return;
        }

        _updatingSelectionUi = true;
        _itemName.Text = item.Name;
        double reachX = Math.Max(_main.Scene.Floor.Length / 2.0, _main.Scene.Items.Max(other => Math.Abs(other.X)) + 1.0);
        double reachZ = Math.Max(_main.Scene.Floor.Width / 2.0, _main.Scene.Items.Max(other => Math.Abs(other.Z)) + 1.0);
        _itemX.MinValue = -reachX;
        _itemX.MaxValue = reachX;
        _itemZ.MinValue = -reachZ;
        _itemZ.MaxValue = reachZ;
        _itemX.Value = item.X;
        _itemZ.Value = item.Z;
        _itemHeight.Value = item.Y;
        _itemYaw.Value = item.Yaw;
        _updatingSelectionUi = false;
        _itemInfo.Text = $"{asset.Name} · {asset.Size[0]:0.##} × {asset.Size[2]:0.##} × {asset.Size[1]:0.##} m";
        FillParams(item, asset);

        var names = SceneTags.TagsOf(item, asset).Select(tag => tag.Name).ToHashSet();
        string[] lines = _main.TagMap.Entries.Where(entry => names.Contains(entry.Name))
            .Select(entry => $"{entry.IecAddress}  {entry.Name}  ({IoList.ModbusText(entry)})").ToArray();
        _itemTags.Text = !asset.IsFunctional ? "Alleen visueel: geen gedrag en geen tags."
            : lines.Length == 0 ? "Werkt in de simulatie; geen eigen tags."
            : string.Join("\n", lines);

        PlaceMarker(_highlight, item, asset);
    }

    /// <summary>One yellow marker on the floor under every selected item when there are several.</summary>
    private void UpdateGroupMarks(bool group)
    {
        int wanted = group && DesignMode ? _selection.Count : 0;
        while (_groupMarks.Count < wanted)
        {
            var mark = new MeshInstance3D { Name = "GroupMark", Mesh = new PlaneMesh { Size = Vector2.One }, MaterialOverride = _highlight.MaterialOverride };
            _main.AddChild(mark);
            _groupMarks.Add(mark);
        }

        for (int i = 0; i < _groupMarks.Count; i++)
        {
            SceneItem? member = i < wanted ? _main.Scene.Find(_selection[i]) : null;
            CatalogAsset? memberAsset = member is null ? null : _main.Catalog.Find(member.Asset);
            _groupMarks[i].Visible = member is not null && memberAsset is not null;
            if (member is not null && memberAsset is not null)
            {
                PlaceMarker(_groupMarks[i], member, memberAsset);
            }
        }
    }

    private static void PlaceMarker(MeshInstance3D marker, SceneItem item, CatalogAsset asset)
    {
        (double minX, double maxX, double minZ, double maxZ) = ScenePicking.Footprint(item, asset);
        ((PlaneMesh)marker.Mesh).Size = new Vector2((float)(maxX - minX) + 0.2f, (float)(maxZ - minZ) + 0.2f);
        marker.RotationDegrees = new Vector3(0.0f, (float)item.Yaw, 0.0f);
        // The footprint need not be centred on the origin: shift the marker to its middle.
        var centre = new Vector3((float)(minX + maxX) / 2.0f, 0.0f, (float)(minZ + maxZ) / 2.0f);
        marker.Position = new Vector3((float)item.X, 0.012f, (float)item.Z) + marker.Basis * centre;
    }

    private string? Rebuild()
    {
        string? error = _main.ApplyScene(_main.Scene);
        if (error is not null)
        {
            ShowStatus(error, isError: true);
        }

        UpdateSelectionUi();
        UpdateHistoryButtons();
        return error;
    }

    private string? Report(string? error, string success)
    {
        ShowStatus(error is null ? success : $"Mislukt: {error}", isError: error is not null);
        return error;
    }

    /// <summary>Shows a line in the status bar, in orange for an error.</summary>
    public void ShowMessage(string text, bool isError) => ShowStatus(text, isError);

    private void ShowStatus(string text, bool isError = false)
    {
        if (_shell is null)
        {
            return;
        }

        _shell.StatusMessage.Text = text;
        _shell.StatusMessage.Modulate = isError ? Warning : Colors.White;
    }

    private static string Display(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
}
