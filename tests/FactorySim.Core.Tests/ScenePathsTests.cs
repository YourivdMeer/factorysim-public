using System;
using System.IO;
using System.Linq;
using FactorySim.Core.Scenes;

namespace FactorySim.Core.Tests;

/// <summary>Plan 22: levels became scenes; old paths, files and folders keep working.</summary>
public class ScenePathsTests
{
    [Theory]
    [InlineData("res://levels/mvp_sorting.fslevel.json", "res://scenes/mvp_sorting.fsscene.json")]
    [InlineData("user://levels/mijn lijn.fslevel.json", "user://scenes/mijn lijn.fsscene.json")]
    [InlineData("user://levels/x.FSLEVEL.JSON", "user://scenes/x.fsscene.json")]
    [InlineData("res://scenes/mvp_sorting.fsscene.json", "res://scenes/mvp_sorting.fsscene.json")]
    [InlineData("C:/export/oud.fslevel.json", "C:/export/oud.fslevel.json")]
    [InlineData("user://levels/notes.txt", "user://levels/notes.txt")]
    [InlineData("user://selftest/a.fslevel.json", "user://selftest/a.fslevel.json")]
    public void Old_example_and_own_paths_move_to_the_scenes_folder_others_stay(string path, string expected) =>
        Assert.Equal(expected, ScenePaths.Upgrade(path));

    [Fact]
    public void The_old_path_of_a_scene_in_the_new_folders_is_known_for_autosave()
    {
        Assert.Equal("user://levels/a.fslevel.json", ScenePaths.Legacy("user://scenes/a.fsscene.json"));
        Assert.Equal("res://levels/a.fslevel.json", ScenePaths.Legacy("res://scenes/a.fsscene.json"));
        Assert.Null(ScenePaths.Legacy("C:/export/a.fsscene.json"));
    }

    [Theory]
    [InlineData("a.fsscene.json", true)]
    [InlineData("a.fslevel.json", true)]
    [InlineData("A.FSSCENE.JSON", true)]
    [InlineData("a.fsblock.json", false)]
    [InlineData("a.json", false)]
    public void Both_extensions_count_as_a_scene_file(string name, bool expected) =>
        Assert.Equal(expected, ScenePaths.IsSceneFile(name));

    [Theory]
    [InlineData("C:/export/oud.fslevel.json", "C:/export/oud.fsscene.json")]
    [InlineData("C:/export/nieuw.fsscene.json", "C:/export/nieuw.fsscene.json")]
    [InlineData("C:/export/geen_extensie", "C:/export/geen_extensie.fsscene.json")]
    public void Saving_always_writes_the_new_extension(string path, string expected) =>
        Assert.Equal(expected, ScenePaths.WithSceneExtension(path));

    [Fact]
    public void The_own_levels_folder_is_copied_once_and_the_old_folder_stays()
    {
        string root = Path.Combine(Path.GetTempPath(), "factorysim_migrate_" + Guid.NewGuid().ToString("N"));
        string old = Path.Combine(root, "levels");
        string fresh = Path.Combine(root, "scenes");
        Directory.CreateDirectory(old);
        try
        {
            File.WriteAllText(Path.Combine(old, "een.fslevel.json"), "1");
            File.WriteAllText(Path.Combine(old, "twee.fslevel.json"), "2");
            File.WriteAllText(Path.Combine(old, "notitie.txt"), "x");

            Assert.Equal(2, ScenePaths.MigrateFolder(old, fresh));
            Assert.Equal(new[] { "een.fsscene.json", "twee.fsscene.json" },
                Directory.GetFiles(fresh, "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal("1", File.ReadAllText(Path.Combine(fresh, "een.fsscene.json")));
            Assert.True(File.Exists(Path.Combine(old, "een.fslevel.json")));

            // A scene removed after the migration does not come back on the next start.
            File.Delete(Path.Combine(fresh, "een.fsscene.json"));
            Assert.Equal(0, ScenePaths.MigrateFolder(old, fresh));
            Assert.False(File.Exists(Path.Combine(fresh, "een.fsscene.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_scene_already_in_the_new_folder_is_not_overwritten_and_no_old_folder_is_fine()
    {
        string root = Path.Combine(Path.GetTempPath(), "factorysim_migrate_" + Guid.NewGuid().ToString("N"));
        string old = Path.Combine(root, "levels");
        string fresh = Path.Combine(root, "scenes");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        try
        {
            Assert.Equal(0, ScenePaths.MigrateFolder(Path.Combine(root, "bestaat_niet"), Path.Combine(root, "leeg")));

            File.WriteAllText(Path.Combine(old, "een.fslevel.json"), "oud");
            File.WriteAllText(Path.Combine(fresh, "een.fsscene.json"), "nieuw");
            Assert.Equal(0, ScenePaths.MigrateFolder(old, fresh));
            Assert.Equal("nieuw", File.ReadAllText(Path.Combine(fresh, "een.fsscene.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_old_level_file_opens_and_is_written_back_as_a_scene()
    {
        var level = new Scene { Name = "Oud" };
        string json = SceneSerializer.ToJson(level);
        Assert.Contains("\"factorysim-scene\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("factorysim-level", json, StringComparison.Ordinal);

        string old = json.Replace("factorysim-scene", "factorysim-level", StringComparison.Ordinal);
        Assert.Equal("Oud", SceneSerializer.FromJson(old).Name);
        Assert.Equal(json, SceneSerializer.ToJson(SceneSerializer.FromJson(old)));
    }

    [Fact]
    public void Autosave_copies_get_the_new_extension_and_old_copies_still_parse()
    {
        string name = AutosavePolicy.FileName("user://scenes/a.fsscene.json", new DateTime(2026, 11, 23, 9, 0, 0));
        Assert.EndsWith(".fsscene.json", name, StringComparison.Ordinal);
        Assert.NotNull(AutosavePolicy.Parse(name));

        string oldName = AutosavePolicy.FileName("user://levels/a.fslevel.json", new DateTime(2026, 11, 20, 9, 0, 0))
            .Replace(".fsscene.json", ".fslevel.json", StringComparison.Ordinal);
        AutosaveCopy? old = AutosavePolicy.Parse(oldName);
        Assert.NotNull(old);
        Assert.Equal(AutosavePolicy.Key("user://levels/a.fslevel.json"), old!.Key);
    }
}
