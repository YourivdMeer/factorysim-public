using System;
using System.Linq;
using FactorySim.Core.Scenes;

namespace FactorySim.Core.Tests;

/// <summary>Plan 16: when the editor writes an automatic copy, what it is called, which copies stay.</summary>
public class AutosavePolicyTests
{
    private const string Own = "user://scenes/mijn_lijn.fsscene.json";
    private const string Example = "res://scenes/mijn_lijn.fsscene.json";

    private static readonly DateTime T0 = new(2026, 11, 21, 14, 32, 5);

    [Fact]
    public void Is_not_due_before_two_minutes()
    {
        Assert.False(AutosavePolicy.IsDue(nowMs: 1_000 + 119_999, lastSaveMs: 1_000, changed: true));
        Assert.True(AutosavePolicy.IsDue(nowMs: 1_000 + 120_000, lastSaveMs: 1_000, changed: true));
    }

    [Fact]
    public void Is_not_due_without_a_change()
    {
        Assert.False(AutosavePolicy.IsDue(nowMs: 10_000_000, lastSaveMs: 0, changed: false));
    }

    [Fact]
    public void Is_time_due_only_looks_at_the_clock()
    {
        Assert.False(AutosavePolicy.IsTimeDue(nowMs: 119_000, lastSaveMs: 0));
        Assert.True(AutosavePolicy.IsTimeDue(nowMs: 120_000, lastSaveMs: 0));
    }

    [Fact]
    public void File_name_has_the_scene_name_a_path_hash_and_the_time()
    {
        string name = AutosavePolicy.FileName(Own, T0);
        Assert.StartsWith("mijn_lijn.", name);
        Assert.EndsWith(".20261121-143205.fsscene.json", name);
        Assert.Matches(@"^mijn_lijn\.[0-9a-f]{8}\.20261121-143205\.fsscene\.json$", name);
    }

    [Fact]
    public void Key_is_stable_between_runs()
    {
        // FNV-1a, not string.GetHashCode: the copies must be found again after a restart.
        Assert.Matches(@"^mijn_lijn\.[0-9a-f]{8}$", AutosavePolicy.Key(Own));
        Assert.Equal(AutosavePolicy.Key(Own), AutosavePolicy.Key(Own));
        Assert.Equal(AutosavePolicy.Key("user://scenes/a.fsscene.json"), "a." + AutosavePolicy.PathHash("user://scenes/a.fsscene.json"));
        Assert.Equal("811c9dc5", AutosavePolicy.PathHash(""));        // FNV-1a offset basis
    }

    [Fact]
    public void Same_name_in_another_folder_gets_another_key()
    {
        Assert.NotEqual(AutosavePolicy.Key(Own), AutosavePolicy.Key(Example));
    }

    [Fact]
    public void Windows_paths_with_other_slashes_or_case_get_the_same_key()
    {
        Assert.Equal(AutosavePolicy.Key(@"C:\Export\Lijn.fsscene.json"), AutosavePolicy.Key("c:/export/Lijn.fsscene.json"));
    }

    [Fact]
    public void Odd_characters_become_underscores()
    {
        string key = AutosavePolicy.Key("user://scenes/Mijn lijn (v2.1)!.fsscene.json");
        Assert.StartsWith("Mijn_lijn__v2_1__.", key);
        Assert.Equal("scene", AutosavePolicy.SafeName(""));
        Assert.Equal("___", AutosavePolicy.SafeName("..."));
        Assert.Equal("Draaitafel_é", AutosavePolicy.SafeName("Draaitafel é"));
    }

    [Fact]
    public void Parse_reads_back_the_key_and_time()
    {
        string name = AutosavePolicy.FileName("user://scenes/Mijn lijn (v2.1)!.fsscene.json", T0);
        AutosaveCopy? copy = AutosavePolicy.Parse(name);
        Assert.NotNull(copy);
        Assert.Equal(AutosavePolicy.Key("user://scenes/Mijn lijn (v2.1)!.fsscene.json"), copy!.Key);
        Assert.Equal(T0, copy.Time);
        Assert.Equal(name, copy.FileName);
    }

    [Theory]
    [InlineData("mijn_lijn.fsscene.json")]
    [InlineData("mijn_lijn.0123abcd.20261121-143205.fsscene.json.tmp")]
    [InlineData("mijn_lijn.0123abcd.20261321-143205.fsscene.json")]
    [InlineData("mijn_lijn.0123abcd.gisteren.fsscene.json")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void Parse_ignores_foreign_files(string name)
    {
        Assert.Null(AutosavePolicy.Parse(name));
    }

    [Fact]
    public void Parse_takes_a_full_path()
    {
        string name = AutosavePolicy.FileName(Own, T0);
        Assert.NotNull(AutosavePolicy.Parse(@"C:\Users\x\AppData\autosave\" + name));
        Assert.NotNull(AutosavePolicy.Parse("user://autosave/" + name));
    }

    [Fact]
    public void To_delete_keeps_the_newest_three_of_this_scene()
    {
        string[] files =
        {
            AutosavePolicy.FileName(Own, T0),
            AutosavePolicy.FileName(Own, T0.AddMinutes(2)),
            AutosavePolicy.FileName(Own, T0.AddMinutes(8)),
            AutosavePolicy.FileName(Own, T0.AddMinutes(4)),
            AutosavePolicy.FileName(Own, T0.AddMinutes(6)),
            AutosavePolicy.FileName(Example, T0.AddMinutes(-60)),   // another scene: never deleted here
            "notes.txt",
        };

        var delete = AutosavePolicy.ToDelete(files, AutosavePolicy.Key(Own), keep: 3);
        Assert.Equal(new[] { files[0], files[1] }.OrderBy(f => f), delete.OrderBy(f => f));
        Assert.DoesNotContain(files[5], delete);
    }

    [Fact]
    public void To_delete_with_keep_zero_takes_all_copies_of_this_scene()
    {
        string[] files = { AutosavePolicy.FileName(Own, T0), AutosavePolicy.FileName(Example, T0), "x.txt" };
        Assert.Equal(new[] { files[0] }, AutosavePolicy.ToDelete(files, AutosavePolicy.Key(Own), keep: 0));
    }

    [Fact]
    public void Newest_newer_than_the_file_is_offered()
    {
        string[] files =
        {
            AutosavePolicy.FileName(Own, T0.AddMinutes(2)),
            AutosavePolicy.FileName(Own, T0.AddMinutes(4)),
            AutosavePolicy.FileName(Example, T0.AddMinutes(10)),
        };

        AutosaveCopy? copy = AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(Own), sceneFileTime: T0);
        Assert.Equal(files[1], copy?.FileName);
    }

    [Fact]
    public void A_copy_older_than_the_file_does_not_count()
    {
        string[] files = { AutosavePolicy.FileName(Own, T0.AddMinutes(-1)), AutosavePolicy.FileName(Own, T0) };
        Assert.Null(AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(Own), sceneFileTime: T0));
    }

    [Fact]
    public void A_copy_of_another_scene_is_never_offered()
    {
        string[] files = { AutosavePolicy.FileName(Example, T0.AddMinutes(5)) };
        Assert.Null(AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(Own), sceneFileTime: T0));
    }

    [Fact]
    public void Without_a_scene_file_any_copy_counts()
    {
        string[] files = { AutosavePolicy.FileName(Own, T0) };
        Assert.NotNull(AutosavePolicy.NewestNewerThan(files, AutosavePolicy.Key(Own), sceneFileTime: DateTime.MinValue));
    }
}
