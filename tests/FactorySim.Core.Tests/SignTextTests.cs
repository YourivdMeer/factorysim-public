using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 19: signs in the scene, a floor text and a sign on a post, with a text per item.</summary>
public class SignTextTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    // ---- the assets

    [Theory]
    [InlineData("floor_text")]
    [InlineData("sign_post")]
    public void Both_signs_are_in_the_library_with_their_own_behaviour_and_no_tags(string assetId)
    {
        CatalogAsset asset = Catalog.Get(assetId);
        var scene = new Scene();
        SceneItem item = scene.Add(asset, 0, 0);

        Assert.Equal(AssetBehaviours.Sign, asset.Behaviour);
        Assert.True(asset.IsPlaceable);
        Assert.Empty(SceneTags.TagsOf(item, asset));
    }

    [Fact]
    public void The_sign_on_a_post_has_a_separate_panel_and_a_collider()
    {
        CatalogAsset asset = Catalog.Get("sign_post");

        Assert.Contains("Panel", asset.Parts);
        Assert.NotNull(asset.Colliders);
        Assert.NotEmpty(asset.Colliders!);
        Assert.Null(Catalog.Get("floor_text").Colliders);
    }

    // ---- the scene field

    [Fact]
    public void A_text_with_accents_and_a_line_break_is_saved_and_opened_again()
    {
        var scene = new Scene();
        SceneItem sign = scene.Add(Catalog.Get("sign_post"), 0, 0);
        scene.SetText(sign.Id, "Station 1\nAfkeur é ë");

        Scene again = Scene.FromJson(scene.ToJson());

        Assert.Equal("Station 1\nAfkeur é ë", again.Items.Single().Text);
    }

    [Fact]
    public void A_scene_without_the_field_loads_as_before_and_does_not_write_it()
    {
        var scene = new Scene();
        SceneItem sign = scene.Add(Catalog.Get("floor_text"), 0, 0);

        Assert.DoesNotContain("\"text\"", scene.ToJson());
        Assert.Null(Scene.FromJson(scene.ToJson()).Items.Single().Text);

        scene.SetText(sign.Id, "Invoer");
        scene.SetText(sign.Id, "   ");                                       // empty clears it

        Assert.Null(scene.Items.Single().Text);
        Assert.DoesNotContain("\"text\"", scene.ToJson());
    }

    [Fact]
    public void Duplicating_and_pasting_keep_the_text()
    {
        var scene = new Scene();
        SceneItem sign = scene.Add(Catalog.Get("sign_post"), 0, 0);
        sign = scene.SetText(sign.Id, "Opdracht 3");

        SceneItem duplicate = scene.Duplicate(sign.Id, 2, 0);
        SceneItem pasted = scene.Paste(sign, 4, 0);

        Assert.Equal("Opdracht 3", duplicate.Text);
        Assert.Equal("Opdracht 3", pasted.Text);
    }

    [Fact]
    public void A_building_block_keeps_the_text()
    {
        var scene = new Scene();
        SceneItem sign = scene.Add(Catalog.Get("sign_post"), 0, 0);
        scene.Add(Catalog.Get("floor_text"), 1, 0);
        scene.SetText(sign.Id, "Station 1");

        SceneBlock block = SceneBlocks.FromSelection(scene, scene.Items.Select(item => item.Id).ToArray(), "Blok", string.Empty);
        SceneBlock again = SceneBlocks.FromJson(SceneBlocks.ToJson(block));

        Assert.Contains(again.Items, item => item.Text == "Station 1");
    }

    [Fact]
    public void An_undo_snapshot_brings_back_the_text_from_before()
    {
        var scene = new Scene();
        SceneItem sign = scene.Add(Catalog.Get("sign_post"), 0, 0);
        scene.SetText(sign.Id, "Oud");
        var history = new SceneHistory();

        history.Record(scene);
        scene.SetText(sign.Id, "Nieuw");
        Scene? undone = history.Undo(scene);
        Scene? redone = history.Redo(undone!);

        Assert.Equal("Oud", undone!.Items.Single().Text);
        Assert.Equal("Nieuw", redone!.Items.Single().Text);
    }

    // ---- limits

    [Fact]
    public void A_text_that_is_too_long_is_cut_off()
    {
        string text = new string('a', SignText.MaxLength + 25);

        Assert.Equal(SignText.MaxLength, SignText.Normalize(text)!.Length);
    }

    [Fact]
    public void At_most_three_lines_are_kept_and_windows_line_breaks_become_plain_ones()
    {
        Assert.Equal("een\ntwee\ndrie", SignText.Normalize("een\r\ntwee\r\ndrie\r\nvier\nvijf"));
        Assert.Equal("een\ntwee", SignText.Normalize("een\rtwee"));
    }

    [Fact]
    public void Surrounding_space_is_trimmed_and_nothing_becomes_null()
    {
        Assert.Equal("Afkeur", SignText.Normalize("  Afkeur \n"));
        Assert.Null(SignText.Normalize(null));
        Assert.Null(SignText.Normalize(" \n "));
    }

    [Fact]
    public void A_text_in_a_file_is_cut_off_when_it_is_read()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("sign_post"), 0, 0);
        string json = scene.ToJson().Replace("\"name\": \"Bordje\"", "\"name\": \"Bordje\",\n      \"text\": \"" + new string('x', 200) + "\"");

        Assert.Equal(SignText.MaxLength, Scene.FromJson(json).Items.Single().Text!.Length);
    }

    // ---- the size setting

    [Fact]
    public void A_sign_has_a_size_setting_small_medium_large_with_medium_as_default()
    {
        ParamField size = Assert.Single(ItemParams.Editable(Catalog.Get("sign_post")));

        Assert.Equal(SignText.SizeParam, size.Name);
        Assert.Equal(0.0, size.Min);
        Assert.Equal(2.0, size.Max);
        Assert.Equal(1.0, size.Default);
        Assert.Equal(SignSize.Small, SignText.SizeOf(0.0));
        Assert.Equal(SignSize.Medium, SignText.SizeOf(1.0));
        Assert.Equal(SignSize.Large, SignText.SizeOf(2.0));
        Assert.Equal(SignSize.Large, SignText.SizeOf(7.0));
    }

    // ---- overlap

    [Fact]
    public void Signs_are_not_compared_for_overlap()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("belt_conveyor_2m"), 0, 0);
        scene.Add(Catalog.Get("floor_text"), 0, 0);
        scene.Add(Catalog.Get("sign_post"), 0, 0);

        Assert.Empty(SceneOverlap.Find(scene, Catalog));
    }
}
