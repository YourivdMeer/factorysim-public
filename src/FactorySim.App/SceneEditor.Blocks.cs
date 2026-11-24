using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Parts;
using FactorySim.App.Ui;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 18: building blocks. A selection of two or more items is saved as a <c>.fsblock.json</c>; the library has a
/// category *Bouwblokken* with the examples first and then the own blocks. Placing works like an item from the
/// library: the see-through group follows the mouse, R turns it, a click places it as one undo step.
/// The format and the placing rules are in <see cref="SceneBlocks"/>.
/// </summary>
public partial class SceneEditor
{
    /// <summary>The id of the library category with the building blocks.</summary>
    public const string BlocksCategory = "blocks";

    /// <summary>The example blocks that come with the app.</summary>
    public const string ExampleBlocksFolder = "res://library/blocks";

    /// <summary>Where the own blocks are kept.</summary>
    public const string UserBlocksFolder = "user://blocks";

    private readonly List<BlockEntry> _paletteBlocks = new();
    private BlockEntry? _placingBlock;
    private float _blockYaw;
    private BlockEntry? _blockToChange;
    private HBoxContainer _blockActions = null!;
    private Button _saveBlockButton = null!;
    private ConfirmationDialog _saveBlockDialog = null!;
    private ConfirmationDialog _renameBlockDialog = null!;
    private ConfirmationDialog _deleteBlockDialog = null!;
    private LineEdit _blockName = null!;
    private LineEdit _blockDescription = null!;
    private LineEdit _blockRename = null!;

    /// <summary>A block in the library: what it holds, its file and whether it is an own one (which can be renamed and removed).</summary>
    public sealed record BlockEntry(SceneBlock Block, string Path, bool Own);

    /// <summary>The folder for own blocks; the self-test points it at a temporary folder.</summary>
    public string BlocksFolder { get; set; } = UserBlocksFolder;

    /// <summary>The blocks shown in the library panel now (only in the category *Bouwblokken*).</summary>
    public IReadOnlyList<BlockEntry> PaletteBlocks => _paletteBlocks;

    /// <summary>True when the button *Bewaar als bouwblok…* is shown.</summary>
    public bool SaveBlockOffered => _saveBlockButton.Visible;

    /// <summary>The examples, then the own blocks, each sorted by name. A file that cannot be read is left out.</summary>
    public IReadOnlyList<BlockEntry> Blocks()
    {
        var entries = new List<BlockEntry>();
        foreach ((string folder, bool own) in new[] { (ExampleBlocksFolder, false), (BlocksFolder, true) })
        {
            var found = new List<BlockEntry>();
            if (!DirAccess.DirExistsAbsolute(folder))
            {
                continue;
            }

            foreach (string file in DirAccess.GetFilesAt(folder).Where(file => file.EndsWith(SceneBlocks.Extension, StringComparison.Ordinal)))
            {
                string path = folder.TrimEnd('/') + "/" + file;
                if (ReadBlock(path) is { } block)
                {
                    found.Add(new BlockEntry(block, path, own));
                }
            }

            entries.AddRange(found.OrderBy(entry => entry.Block.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        return entries;
    }

    /// <summary>Reads a block file (res://, user:// or a file system path); null when it cannot be read.</summary>
    public static SceneBlock? ReadBlock(string path)
    {
        try
        {
            return FileAccess.FileExists(path) ? SceneBlocks.FromJson(FileAccess.GetFileAsString(path)) : null;
        }
        catch (SceneException e)
        {
            GD.PushWarning($"Bouwblok {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves the selection (two or more items) as an own block under a file name that follows from the name, next to
    /// the blocks that are there. Returns the path, or null with a message in the status bar.
    /// </summary>
    public string? SaveSelectionAsBlock(string name, string description)
    {
        if (_selection.Count < 2)
        {
            ShowStatus("Selecteer eerst twee of meer onderdelen.", isError: true);
            return null;
        }

        SceneBlock block;
        try
        {
            block = SceneBlocks.FromSelection(_main.Scene, _selection, name, description);
        }
        catch (SceneException e)
        {
            ShowStatus(e.Message, isError: true);
            return null;
        }

        string folder = ProjectSettings.GlobalizePath(BlocksFolder);
        System.IO.Directory.CreateDirectory(folder);
        string fileName = SceneBlocks.FileName(block.Name);
        string stem = fileName[..^SceneBlocks.Extension.Length];
        for (int number = 2; System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)); number++)
        {
            fileName = $"{stem}_{number}{SceneBlocks.Extension}";
        }

        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, fileName), SceneBlocks.ToJson(block));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Bouwblok niet opgeslagen: {e.Message}", isError: true);
            return null;
        }

        if (_categoryId == BlocksCategory)
        {
            FillPalette();
        }

        string path = BlocksFolder.TrimEnd('/') + "/" + fileName;
        ShowStatus($"Bouwblok '{block.Name}' bewaard ({block.Items.Count} onderdelen). Je vindt het in de library onder Bouwblokken.");
        return path;
    }

    /// <summary>
    /// Places a block with its middle at (x, z) on the grid, turned by <paramref name="yaw"/> degrees, as one undo step,
    /// and selects the new items. Returns them, or null when the scene cannot run with them.
    /// </summary>
    public IReadOnlyList<SceneItem>? PlaceBlock(SceneBlock block, float x, float z, float yaw = 0.0f)
    {
        _history.Record(_main.Scene);
        BlockPlacement placed = SceneBlocks.Place(_main.Scene, block, _main.Catalog, x, z, yaw);
        if (placed.Items.Count == 0)
        {
            _history.DropLast();
            ShowStatus($"'{block.Name}' heeft geen onderdelen die de library kent.", isError: true);
            return null;
        }

        if (Rebuild() is not null)
        {
            _main.Scene.RemoveMany(placed.Items.Select(item => item.Id).ToArray());
            _history.DropLast();
            Rebuild();
            return null;
        }

        SelectMany(placed.Items.Select(item => item.Id).ToArray());
        ShowStatus(placed.Skipped.Count == 0
            ? $"Bouwblok '{block.Name}' geplaatst: {placed.Items.Count} onderdelen."
            : $"Bouwblok '{block.Name}' geplaatst; overgeslagen omdat de library ze niet kent: {string.Join(", ", placed.Skipped)}.");
        return placed.Items;
    }

    /// <summary>Gives an own block another name (the file keeps its name). Returns an error text, or null.</summary>
    public string? RenameBlock(string path, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return Report("Een bouwblok heeft een naam nodig.", string.Empty);
        }

        if (ReadBlock(path) is not { } block)
        {
            return Report($"Bouwblok {path} is niet te lezen.", string.Empty);
        }

        try
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(path), SceneBlocks.ToJson(block with { Name = name }));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return Report(e.Message, string.Empty);
        }

        FillPalette();
        return Report(null, $"Bouwblok hernoemd tot '{name}'.");
    }

    /// <summary>Removes an own block file. Returns an error text, or null.</summary>
    public string? DeleteBlock(string path)
    {
        try
        {
            System.IO.File.Delete(ProjectSettings.GlobalizePath(path));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return Report(e.Message, string.Empty);
        }

        CancelPlacing();
        FillPalette();
        return Report(null, "Bouwblok verwijderd.");
    }

    private void BuildBlockActions(Container column)
    {
        _blockActions = new HBoxContainer { Visible = false };
        _blockActions.AddChild(Shell.MakeButton("Hernoem", AskRenameBlock, "Geef het gekozen eigen bouwblok een andere naam"));
        _blockActions.AddChild(Shell.MakeButton("Verwijder", AskDeleteBlock, "Verwijder het gekozen eigen bouwblok"));
        column.AddChild(_blockActions);
    }

    private void BuildSaveBlockButton(Container column)
    {
        _saveBlockButton = Shell.MakeButton("Bewaar als bouwblok…", AskSaveBlock,
            "Bewaar de geselecteerde onderdelen als bouwblok om ze later opnieuw te plaatsen");
        _saveBlockButton.Visible = false;
        column.AddChild(_saveBlockButton);
    }

    private void BuildBlockDialogs()
    {
        _saveBlockDialog = new ConfirmationDialog
        {
            Title = "Bewaar als bouwblok", OkButtonText = "Bewaar", CancelButtonText = "Annuleren", Size = new Vector2I(420, 170),
        };
        var column = new VBoxContainer();
        _blockName = new LineEdit { PlaceholderText = "Naam, bijvoorbeeld Pusherstation" };
        _blockDescription = new LineEdit { PlaceholderText = "Beschrijving (mag leeg)" };
        column.AddChild(_blockName);
        column.AddChild(_blockDescription);
        _saveBlockDialog.AddChild(column);
        _saveBlockDialog.Confirmed += () => SaveSelectionAsBlock(_blockName.Text, _blockDescription.Text);
        AddChild(_saveBlockDialog);

        _renameBlockDialog = new ConfirmationDialog
        {
            Title = "Bouwblok hernoemen", OkButtonText = "Hernoem", CancelButtonText = "Annuleren", Size = new Vector2I(380, 120),
        };
        _blockRename = new LineEdit();
        _renameBlockDialog.AddChild(_blockRename);
        _renameBlockDialog.Confirmed += () =>
        {
            if (_blockToChange is not null)
            {
                RenameBlock(_blockToChange.Path, _blockRename.Text);
            }
        };
        AddChild(_renameBlockDialog);

        _deleteBlockDialog = new ConfirmationDialog
        {
            Title = "Bouwblok verwijderen", OkButtonText = "Verwijder", CancelButtonText = "Annuleren", Size = new Vector2I(380, 120),
        };
        _deleteBlockDialog.Confirmed += () =>
        {
            if (_blockToChange is not null)
            {
                DeleteBlock(_blockToChange.Path);
            }
        };
        AddChild(_deleteBlockDialog);
    }

    private void AskSaveBlock()
    {
        _blockName.Text = string.Empty;
        _blockDescription.Text = string.Empty;
        _saveBlockDialog.PopupCentered();
        _blockName.GrabFocus();
    }

    /// <summary>The own block chosen in the library, or null with a hint.</summary>
    private BlockEntry? ChosenOwnBlock()
    {
        int[] chosen = _palette.GetSelectedItems();
        BlockEntry? entry = chosen.Length == 1 && chosen[0] < _paletteBlocks.Count ? _paletteBlocks[chosen[0]] : _placingBlock;
        if (entry is not { Own: true })
        {
            ShowStatus("Kies eerst een eigen bouwblok in de library; de voorbeelden kun je niet wijzigen.", isError: true);
            return null;
        }

        return entry;
    }

    private void AskRenameBlock()
    {
        if (ChosenOwnBlock() is not { } entry)
        {
            return;
        }

        _blockToChange = entry;
        _blockRename.Text = entry.Block.Name;
        _renameBlockDialog.PopupCentered();
        _blockRename.GrabFocus();
    }

    private void AskDeleteBlock()
    {
        if (ChosenOwnBlock() is not { } entry)
        {
            return;
        }

        _blockToChange = entry;
        _deleteBlockDialog.DialogText = $"Bouwblok '{entry.Block.Name}' verwijderen? Dit kan niet ongedaan worden gemaakt.";
        _deleteBlockDialog.PopupCentered();
    }

    /// <summary>Fills the library panel with the blocks instead of the assets.</summary>
    private void FillBlockPalette()
    {
        _paletteBlocks.Clear();
        string search = _paletteSearch.Text.Trim();
        foreach (BlockEntry entry in Blocks())
        {
            if (search.Length > 0
                && !entry.Block.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                && !entry.Block.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            int index = _palette.AddItem((entry.Own ? "" : "★ ") + entry.Block.Name);
            _palette.SetItemTooltip(index, $"{entry.Block.Name} — {entry.Block.Items.Count} onderdelen"
                + (entry.Block.Description.Length > 0 ? $"\n{entry.Block.Description}" : string.Empty)
                + (entry.Own ? "\nEigen bouwblok" : "\nVoorbeeld"));
            _paletteBlocks.Add(entry);
        }

        _blockActions.Visible = true;
        _assetInfo.Text = _paletteBlocks.Count == 0
            ? "Geen bouwblokken gevonden."
            : "★ = voorbeeld. Bewaar zelf een blok met twee of meer geselecteerde onderdelen.";
    }

    private void StartPlacingBlock(int index)
    {
        if (index < 0 || index >= _paletteBlocks.Count)
        {
            return;
        }

        BlockEntry entry = _paletteBlocks[index];
        CancelPlacing(keepPaletteChoice: true);
        Select(null);
        _placingBlock = entry;
        _blockYaw = 0.0f;
        _ghost = new Node3D { Name = "PlacingGhost" };
        foreach (SceneItem item in entry.Block.Items)
        {
            if (_main.Catalog.Find(item.Asset) is null)
            {
                continue;
            }

            Node3D model = Models.Instantiate(item.Asset);
            model.Position = new Vector3((float)item.X, (float)item.Y, (float)item.Z);
            model.RotationDegrees = new Vector3(0.0f, (float)item.Yaw, 0.0f);
            _ghost.AddChild(model);
        }

        MakeTranslucent(_ghost);
        _main.AddChild(_ghost);
        _assetInfo.Text = $"{entry.Block.Name} · {entry.Block.Items.Count} onderdelen"
            + (entry.Block.Description.Length > 0 ? $"\n{entry.Block.Description}" : string.Empty);
        ShowStatus($"Klik op de vloer om bouwblok '{entry.Block.Name}' te plaatsen. R draait, Esc stopt.");
    }

    /// <summary>Moves the see-through block to the mouse; false when no block is being placed.</summary>
    private bool MoveBlockGhost(Vector3 point)
    {
        if (_placingBlock is null || _ghost is null)
        {
            return false;
        }

        _ghost.Position = new Vector3((float)Scene.Snap(point.X), 0.0f, (float)Scene.Snap(point.Z));
        _ghost.RotationDegrees = new Vector3(0.0f, _blockYaw, 0.0f);
        return true;
    }

    private void TurnBlockGhost()
    {
        _blockYaw = (_blockYaw + 90.0f) % 360.0f;
        if (_ghost is not null)
        {
            _ghost.RotationDegrees = new Vector3(0.0f, _blockYaw, 0.0f);
        }
    }

    /// <summary>A click on the floor while a block is being placed; false when no block is being placed.</summary>
    private bool ClickBlock(Vector3 point)
    {
        if (_placingBlock is null)
        {
            return false;
        }

        if (!_main.Scene.OnFloor(point.X, point.Z))
        {
            ShowStatus("Dat is buiten de vloer. Kies een plek op de vloer.", isError: true);
            return true;
        }

        PlaceBlock(_placingBlock.Block, point.X, point.Z, _blockYaw);
        return true;
    }
}
