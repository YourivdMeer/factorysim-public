using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.App.Ui;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>One scene file in the gallery; its summary is read when the card first comes into view.</summary>
public sealed class GalleryEntry
{
    public GalleryEntry(string group, string path)
    {
        Group = group;
        Path = path;
    }

    public string Group { get; }

    public string Path { get; }

    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

    public SceneSummary? Summary { get; set; }
}

/// <summary>
/// Plan 20: the scene gallery behind *Scene* → *Openen…*. Three groups (examples, own scenes, recent), a card
/// per scene with a floor plan, name, description, counts and date, a search field, and *Bladeren…* for a file
/// elsewhere. Also the scene description (*Scene* → *Beschrijving…*).
/// </summary>
public partial class SceneEditor
{
    public const string GroupExamples = "Voorbeelden";
    public const string GroupOwn = "Eigen scènes";
    public const string GroupRecent = "Recent";

    private const string ExamplesFolder = ScenePaths.ExamplesFolder;

    /// <summary>Summaries read before, per file and its time, so the gallery opens quickly the next time.</summary>
    private static readonly Dictionary<string, (ulong Time, SceneSummary Summary)> SummaryCache = new();

    private AcceptDialog _galleryDialog = null!;
    private LineEdit _gallerySearch = null!;
    private VBoxContainer _galleryGroups = null!;
    private ScrollContainer _galleryScroll = null!;
    private readonly List<(GalleryEntry Entry, PanelContainer Card)> _galleryCards = new();
    private GalleryEntry? _gallerySelected;
    private ConfirmationDialog _descriptionDialog = null!;
    private TextEdit _descriptionText = null!;

    /// <summary>The scene files of the gallery, in groups; nothing is read yet. A self-test passes its own folder.</summary>
    public IReadOnlyList<GalleryEntry> GalleryEntries(string? ownFolder = null)
    {
        var entries = new List<GalleryEntry>();
        entries.AddRange(SceneFiles(ExamplesFolder).Select(path => new GalleryEntry(GroupExamples, path)));
        entries.AddRange(SceneFiles(ownFolder ?? UserScenesFolder).Select(path => new GalleryEntry(GroupOwn, path)));
        IEnumerable<string> recent = _main.SelfTestRun && ownFolder is null ? Array.Empty<string>() : _main.RecentScenes;
        entries.AddRange(recent.Where(FileAccess.FileExists).Select(path => new GalleryEntry(GroupRecent, path)));
        return entries;
    }

    /// <summary>Reads the summary of a gallery entry, from the cache when the file has not changed.</summary>
    public SceneSummary Summarize(GalleryEntry entry)
    {
        bool exists = FileAccess.FileExists(entry.Path);
        ulong time = exists ? FileAccess.GetModifiedTime(entry.Path) : 0;
        if (exists && SummaryCache.TryGetValue(entry.Path, out var cached) && cached.Time == time)
        {
            return entry.Summary = cached.Summary;
        }

        DateTime? fileTime = time == 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)time).LocalDateTime;
        SceneSummary summary = exists
            ? SceneSummary.FromJson(FileAccess.GetFileAsString(entry.Path), _main.Catalog, fileTime, entry.FileName)
            : SceneSummary.FromJson(null, _main.Catalog, null, entry.FileName) with { Error = "Het bestand bestaat niet meer." };
        SummaryCache[entry.Path] = (time, summary);
        return entry.Summary = summary;
    }

    /// <summary>Opens the scene of a card, as *Openen…* does. Returns an error text, or null.</summary>
    public string? OpenFromGallery(GalleryEntry entry)
    {
        string? error = LoadFrom(entry.Path);
        if (error is null)
        {
            _galleryDialog.Hide();
        }

        return error;
    }

    /// <summary>Gives the scene a name, as typing it in the toolbar does (saving takes the name from there).</summary>
    public void SetSceneName(string name)
    {
        _sceneName.Text = name;
        _main.Scene.Name = name;
    }

    /// <summary>Sets the description of the scene: one step for undo, nothing when it does not change.</summary>
    public void SetDescription(string? text)
    {
        string before = _main.Scene.Description;
        _history.Record(_main.Scene);
        _main.Scene.SetDescription(text);
        if (_main.Scene.Description == before)
        {
            _history.DropLast();
            return;
        }

        UpdateHistoryButtons();
        ShowStatus(_main.Scene.Description.Length == 0 ? "Beschrijving gewist." : "Beschrijving aangepast.");
    }

    /// <summary>Shows the gallery with the current files; summaries are read as the cards come into view.</summary>
    public void ShowGallery()
    {
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(UserScenesFolder));
        _gallerySearch.Text = string.Empty;
        FillGallery(GalleryEntries());
        _galleryDialog.PopupCentered(new Vector2I(940, 640));
    }

    /// <summary>For the self-test: the gallery with these entries, every card read, ready for a screenshot.</summary>
    public void ShowGalleryWith(IReadOnlyList<GalleryEntry> entries)
    {
        FillGallery(entries);
        foreach ((GalleryEntry entry, PanelContainer card) in _galleryCards)
        {
            FillCard(entry, card);
        }

        _galleryDialog.PopupCentered(new Vector2I(940, 640));
    }

    public void HideGallery() => _galleryDialog.Hide();

    private static IEnumerable<string> SceneFiles(string folder)
    {
        using DirAccess? dir = DirAccess.Open(folder);
        if (dir is null)
        {
            return Array.Empty<string>();
        }

        return dir.GetFiles().Where(ScenePaths.IsSceneFile)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => $"{folder.TrimEnd('/')}/{name}").ToArray();
    }

    private void BuildGalleryDialog()
    {
        _galleryDialog = new AcceptDialog { Title = "Scène openen", OkButtonText = "Sluiten", MinSize = new Vector2I(640, 420) };
        _galleryDialog.AddButton("Bladeren…", right: false, action: "browse");
        _galleryDialog.AddButton("Openen", right: true, action: "open");
        _galleryDialog.CustomAction += action =>
        {
            if (action == "open" && _gallerySelected is not null)
            {
                OpenFromGallery(_gallerySelected);
            }
            else if (action == "browse")
            {
                _galleryDialog.Hide();
                AskFile(save: false, own: false, path => LoadFrom(path));
            }
        };

        var column = new VBoxContainer();
        _gallerySearch = new LineEdit { PlaceholderText = "Zoek op naam of beschrijving", ClearButtonEnabled = true };
        _gallerySearch.TextChanged += _ => ApplyGallerySearch();
        column.AddChild(_gallerySearch);
        _galleryScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _galleryGroups = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _galleryScroll.AddChild(_galleryGroups);
        column.AddChild(_galleryScroll);
        _galleryDialog.AddChild(column);

        // Read the cards that came into view, a few per frame.
        var reader = new Timer { WaitTime = 0.05, Autostart = true };
        reader.Timeout += ReadVisibleCards;
        _galleryDialog.AddChild(reader);
        AddChild(_galleryDialog);

        _descriptionDialog = new ConfirmationDialog
        {
            Title = "Beschrijving van de scène", OkButtonText = "Toepassen", CancelButtonText = "Annuleren", Size = new Vector2I(460, 240),
        };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"Wat is deze scène, wat moet de PLC doen? Hoogstens {Scene.MaxDescription} tekens; de galerij toont de eerste twee regels." , AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420.0f, 0.0f) });
        _descriptionText = new TextEdit { CustomMinimumSize = new Vector2(420.0f, 120.0f), WrapMode = TextEdit.LineWrappingMode.Boundary };
        box.AddChild(_descriptionText);
        _descriptionDialog.AddChild(box);
        _descriptionDialog.Confirmed += () => SetDescription(_descriptionText.Text);
        AddChild(_descriptionDialog);
    }

    private void AskDescription()
    {
        _descriptionText.Text = _main.Scene.Description;
        _descriptionDialog.PopupCentered();
    }

    private void FillGallery(IReadOnlyList<GalleryEntry> entries)
    {
        foreach (Node child in _galleryGroups.GetChildren())
        {
            _galleryGroups.RemoveChild(child);
            child.QueueFree();
        }

        _galleryCards.Clear();
        _gallerySelected = null;
        foreach (string group in new[] { GroupExamples, GroupOwn, GroupRecent })
        {
            GalleryEntry[] inGroup = entries.Where(entry => entry.Group == group).ToArray();
            _galleryGroups.AddChild(Shell.Heading(group));
            if (inGroup.Length == 0)
            {
                _galleryGroups.AddChild(new Label { Text = group == GroupOwn ? "Nog geen eigen scènes." : "Geen.", Modulate = Shell.Muted });
                continue;
            }

            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 10);
            flow.AddThemeConstantOverride("v_separation", 10);
            foreach (GalleryEntry entry in inGroup)
            {
                PanelContainer card = NewCard(entry);
                _galleryCards.Add((entry, card));
                flow.AddChild(card);
            }

            _galleryGroups.AddChild(flow);
        }
    }

    private PanelContainer NewCard(GalleryEntry entry)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(214.0f, 0.0f), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, TooltipText = entry.Path, MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", CardStyle(selected: false));
        var column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(new FloorPlanView { Name = "Plan", CustomMinimumSize = new Vector2(200.0f, 120.0f), MouseFilter = Control.MouseFilterEnum.Ignore });
        column.AddChild(new Label { Name = "Title", Text = entry.FileName, ClipText = true, CustomMinimumSize = new Vector2(200.0f, 0.0f), MouseFilter = Control.MouseFilterEnum.Ignore });
        column.AddChild(new Label
        {
            Name = "Description", Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2,
            CustomMinimumSize = new Vector2(200.0f, 0.0f), Modulate = Shell.Muted, MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        column.AddChild(new Label { Name = "Info", Text = "…", Modulate = Shell.Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200.0f, 0.0f), MouseFilter = Control.MouseFilterEnum.Ignore });
        card.AddChild(column);
        card.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            {
                SelectCard(entry);
                if (click.DoubleClick)
                {
                    Callable.From(() => OpenFromGallery(entry)).CallDeferred();
                }
            }
        };
        return card;
    }

    private static StyleBoxFlat CardStyle(bool selected) => new()
    {
        BgColor = new Color(0.17f, 0.18f, 0.21f),
        BorderColor = selected ? Shell.Accent : new Color(0.28f, 0.30f, 0.34f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 5, ContentMarginBottom = 5,
    };

    private void SelectCard(GalleryEntry entry)
    {
        _gallerySelected = entry;
        foreach ((GalleryEntry other, PanelContainer card) in _galleryCards)
        {
            card.AddThemeStyleboxOverride("panel", CardStyle(other == entry));
        }
    }

    private void ReadVisibleCards()
    {
        if (!_galleryDialog.Visible)
        {
            return;
        }

        Rect2 view = _galleryScroll.GetGlobalRect();
        int read = 0;
        foreach ((GalleryEntry entry, PanelContainer card) in _galleryCards)
        {
            if (entry.Summary is null && card.IsVisibleInTree() && card.GetGlobalRect().Intersects(view) && read < 4)
            {
                FillCard(entry, card);
                read++;
            }
        }
    }

    private void FillCard(GalleryEntry entry, PanelContainer card)
    {
        SceneSummary summary = entry.Summary ?? Summarize(entry);
        card.GetNode<FloorPlanView>("Column/Plan").Summary = summary;
        card.GetNode<Label>("Column/Title").Text = summary.Name;
        var description = card.GetNode<Label>("Column/Description");
        var info = card.GetNode<Label>("Column/Info");
        if (summary.Error is not null)
        {
            description.Text = "Kan niet worden geopend: " + summary.Error;
            description.Modulate = Shell.Warn;
            info.Text = string.Empty;
            return;
        }

        description.Text = summary.DescriptionPreview;
        info.Text = $"{summary.ItemCount} {(summary.ItemCount == 1 ? "onderdeel" : "onderdelen")} · {summary.TagCount} {(summary.TagCount == 1 ? "tag" : "tags")}"
            + (summary.FileTime is { } time ? $" · {time:dd-MM-yyyy}" : string.Empty);
        if (summary.Warning is { } warning)
        {
            info.Text += "\n⚠ " + warning;
            info.Modulate = Shell.Warn;
        }
    }

    private void ApplyGallerySearch()
    {
        string text = _gallerySearch.Text;
        foreach ((GalleryEntry entry, PanelContainer card) in _galleryCards)
        {
            if (text.Trim().Length > 0 && entry.Summary is null)
            {
                FillCard(entry, card);                                       // searching needs the name and the description
            }

            card.Visible = entry.Summary is null || SceneSummary.Search(new[] { entry.Summary }, text).Count == 1;
        }
    }
}
