using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>Plan 20, part of the "editor" self-test: the scene gallery over a folder of its own.</summary>
public partial class SelfTest
{
    private const string GalleryFolder = "user://selftest/gallery";
    private const string GalleryDescription = "Galerijtest: sorteer op hoogte.\nTweede regel.\nDerde regel.";

    /// <summary>Saves two scenes in a folder, reads the gallery list, opens one through its card, then adds a broken file.</summary>
    private bool RunEditorGallery()
    {
        string folder = ProjectSettings.GlobalizePath(GalleryFolder);
        if (System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }

        System.IO.Directory.CreateDirectory(folder);

        // 1. Two scenes in the folder, one with a description (one step for undo).
        Editor.NewScene();
        Editor.SetSceneName("Galerij een");
        Editor.Place("belt_conveyor_4m", 0.0f, 0.0f);
        Editor.Place("diffuse_sensor", 1.0f, -0.6f);
        Editor.SetDescription(GalleryDescription);
        bool described = Main.Scene.Description == GalleryDescription && Editor.Undo() && Main.Scene.Description.Length == 0
            && Editor.Redo() && Main.Scene.Description == GalleryDescription;
        bool savedFirst = Editor.SaveTo($"{GalleryFolder}/een{SceneEditor.FileExtension}") is null;
        int firstTags = Main.TagMap.Entries.Count;

        Editor.NewScene();
        Editor.SetSceneName("Galerij twee");
        Editor.Place("roller_conveyor_2m", 0.0f, 0.0f);
        bool savedSecond = Editor.SaveTo($"{GalleryFolder}/twee{SceneEditor.FileExtension}") is null;

        // 2. The list of the gallery: the two examples, and two cards in the own folder with the right counts.
        GalleryEntry[] entries = Editor.GalleryEntries(GalleryFolder).ToArray();
        GalleryEntry[] own = entries.Where(entry => entry.Group == SceneEditor.GroupOwn).ToArray();
        SceneSummary[] summaries = own.Select(Editor.Summarize).ToArray();
        bool examples = entries.Count(entry => entry.Group == SceneEditor.GroupExamples) == 2
            && entries.Where(entry => entry.Group == SceneEditor.GroupExamples).All(entry => Editor.Summarize(entry).Description.Length > 0);
        bool listed = summaries.Length == 2
            && summaries[0] is { Name: "Galerij een", ItemCount: 2, Error: null } && summaries[0].TagCount == firstTags
            && summaries[0].DescriptionPreview == "Galerijtest: sorteer op hoogte.\nTweede regel."
            && summaries[1] is { Name: "Galerij twee", ItemCount: 1, Description.Length: 0, Error: null }
            && summaries.All(summary => summary.FileTime is not null && summary.Footprints.Count == summary.ItemCount);
        foreach (SceneSummary summary in summaries)
        {
            GD.Print($"SELFTEST editor gallery card: '{summary.Name}' items={summary.ItemCount} tags={summary.TagCount} "
                + $"plan={summary.Footprints.Count} time={summary.FileTime} error={summary.Error} preview='{summary.DescriptionPreview.Replace('\n', '|')}'");
        }

        GD.Print($"SELFTEST editor gallery: entries={entries.Length} own={own.Length} examples={examples} "
            + $"first={summaries.FirstOrDefault()?.ItemCount}/{summaries.FirstOrDefault()?.TagCount}");

        // 3. Open the first through the operation behind its card.
        bool opened = Editor.OpenFromGallery(own[0]) is null && Main.Scene.Name == "Galerij een"
            && Main.Scene.Description == GalleryDescription && Main.Scene.Items.Count == 2;

        // 4. A broken file in the folder: listed with a message; opening it leaves the scene as it is.
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "kapot" + SceneEditor.FileExtension), "{ kapot");
        string before = Main.Scene.ToJson();
        GalleryEntry? broken = Editor.GalleryEntries(GalleryFolder).FirstOrDefault(entry => entry.FileName.StartsWith("kapot"));
        bool brokenListed = broken is not null && Editor.Summarize(broken).Error is not null
            && Editor.GalleryEntries(GalleryFolder).Count(entry => entry.Group == SceneEditor.GroupOwn) == 3;
        bool unchanged = broken is not null && Editor.OpenFromGallery(broken) is not null && Main.Scene.ToJson() == before;

        bool ok = described && savedFirst && savedSecond && examples && listed && opened && brokenListed && unchanged;
        GD.Print($"SELFTEST editor gallery: described={described} saved={savedFirst && savedSecond} listed={listed} "
            + $"opened={opened} broken={brokenListed} unchanged={unchanged}");
        return ok;
    }
}
