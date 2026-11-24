using FactorySim.Core.Scenes;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 04, part A: every emitter can emit any item that fits through its portal.</summary>
public class EmitterItemTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    // ---- the scene field

    [Fact]
    public void The_chosen_item_is_saved_and_opened_again_and_survives_a_copy()
    {
        var scene = new Scene();
        SceneItem emitter = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);
        scene.SetEmits(emitter.Id, "stackable_box");

        Scene again = Scene.FromJson(scene.ToJson());
        SceneItem copy = again.Duplicate(emitter.Id, 2, 0);

        Assert.Equal("stackable_box", again.Items.Single(item => item.Id == emitter.Id).Emits);
        Assert.Equal("stackable_box", copy.Emits);
    }

    [Fact]
    public void A_scene_without_the_field_loads_as_before_and_does_not_write_it()
    {
        var scene = new Scene();
        SceneItem emitter = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);

        Assert.DoesNotContain("emits", scene.ToJson());
        Assert.Null(Scene.FromJson(scene.ToJson()).Items.Single().Emits);

        scene.SetEmits(emitter.Id, "box_small");
        scene.SetEmits(emitter.Id, null);                                   // back to the default

        Assert.DoesNotContain("emits", scene.ToJson());
    }

    [Fact]
    public void An_undo_snapshot_brings_back_the_item_that_was_emitted_before()
    {
        var scene = new Scene();
        SceneItem emitter = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);
        var history = new SceneHistory();

        history.Record(scene);
        scene.SetEmits(emitter.Id, "pallet_square");
        Scene? undone = history.Undo(scene);
        Scene? redone = history.Redo(undone!);

        Assert.Null(undone!.Items.Single().Emits);
        Assert.Equal("pallet_square", redone!.Items.Single().Emits);
    }

    // ---- what an emitter emits

    [Fact]
    public void Without_a_choice_the_catalogue_decides_and_a_choice_overrides_it()
    {
        var scene = new Scene();
        SceneItem gantry = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);
        SceneItem pallets = scene.Add(Catalog.Get("emitter_pallet"), 3, 0);
        SceneItem raw = scene.Add(Catalog.Get("emitter_raw_blue"), 6, 0);

        Assert.Null(EmitterItems.Resolve(gantry, Catalog.Get("emitter_gantry"), Catalog));                // the two standard boxes
        Assert.Equal("pallet", EmitterItems.Resolve(pallets, Catalog.Get("emitter_pallet"), Catalog)?.Id);
        Assert.Equal("raw_material_blue", EmitterItems.Resolve(raw, Catalog.Get("emitter_raw_blue"), Catalog)?.Id);

        gantry = scene.SetEmits(gantry.Id, "stackable_box");
        raw = scene.SetEmits(raw.Id, "product_base_green");

        Assert.Equal("stackable_box", EmitterItems.Resolve(gantry, Catalog.Get("emitter_gantry"), Catalog)?.Id);
        Assert.Equal("product_base_green", EmitterItems.Resolve(raw, Catalog.Get("emitter_raw_blue"), Catalog)?.Id);
        Assert.Null(EmitterItems.Resolve(gantry with { Emits = "no_such_item" }, Catalog.Get("emitter_gantry"), Catalog));
    }

    [Fact]
    public void The_gantry_offers_every_item_that_fits_through_it_and_no_pallets()
    {
        string[] ids = EmitterItems.Choices(Catalog.Get("emitter_gantry"), Catalog).Select(asset => asset.Id).ToArray();

        Assert.Contains("box_small", ids);
        Assert.Contains("box_large", ids);
        Assert.Contains("stackable_box", ids);
        Assert.Contains("raw_material_metal", ids);
        Assert.Contains("product_base_blue", ids);
        Assert.Contains("product_lid_green", ids);
        Assert.Contains("final_product_metal", ids);
        Assert.DoesNotContain("pallet", ids);
        Assert.DoesNotContain("pallet_square", ids);
        Assert.DoesNotContain("box_low", ids);                              // the two standard boxes are the default, not a choice
        Assert.DoesNotContain("box_high", ids);
        Assert.Equal(ids.Distinct().Count(), ids.Length);
    }

    [Fact]
    public void The_pallet_portal_offers_both_pallets_and_the_small_items_too()
    {
        string[] ids = EmitterItems.Choices(Catalog.Get("emitter_pallet"), Catalog).Select(asset => asset.Id).ToArray();

        Assert.Contains("pallet", ids);
        Assert.Contains("pallet_square", ids);
        Assert.Contains("box_small", ids);
    }

    [Fact]
    public void The_spawn_volume_is_the_size_of_the_item_plus_ten_centimetres_and_the_mass_comes_from_the_item()
    {
        CatalogAsset pallet = Catalog.Get("pallet");
        CatalogAsset box = Catalog.Get("box_small");
        var scene = new Scene();
        SceneItem gantry = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);

        (double length, double width) = EmitterItems.VolumeFor(pallet);

        Assert.Equal(pallet.Size[0] + 0.1, length, 6);
        Assert.Equal(pallet.Size[2] + 0.1, width, 6);
        Assert.Equal(15.0, EmitterItems.MassFor(gantry, Catalog.Get("emitter_gantry"), pallet));
        Assert.True(EmitterItems.MassFor(gantry, Catalog.Get("emitter_gantry"), box) > 0.0);
        // A mass the user typed in for this emitter wins.
        scene.SetParam(gantry.Id, "itemMass", 7.5);
        Assert.Equal(7.5, EmitterItems.MassFor(scene.Items.Single(), Catalog.Get("emitter_gantry"), box));
    }

    [Fact]
    public void Every_item_that_an_emitter_can_choose_has_a_mass_in_the_catalogue()
    {
        foreach (CatalogAsset item in EmitterItems.Choices(Catalog.Get("emitter_pallet"), Catalog))
        {
            Assert.True(item.Params.ContainsKey("mass"), $"{item.Id} has no mass");
            Assert.True(item.Param("mass", 0.0) > 0.0);
        }
    }

    // ---- problems

    [Fact]
    public void A_scene_with_an_item_that_does_not_exist_or_does_not_fit_reports_it()
    {
        var scene = new Scene();
        SceneItem first = scene.Add(Catalog.Get("emitter_gantry"), 0, 0);
        SceneItem second = scene.Add(Catalog.Get("emitter_gantry"), 3, 0);
        SceneItem third = scene.Add(Catalog.Get("emitter_gantry"), 6, 0);
        SceneItem fourth = scene.Add(Catalog.Get("emitter_gantry"), 9, 0);
        scene.SetEmits(first.Id, "no_such_item");
        scene.SetEmits(second.Id, "belt_conveyor_2m");        // not an item
        scene.SetEmits(third.Id, "pallet");                    // too big for this portal
        scene.SetEmits(fourth.Id, "box_medium");               // fine

        IReadOnlyList<string> problems = SceneTags.Problems(scene, Catalog);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, text => text.Contains(first.Name) && text.Contains("no_such_item"));
        Assert.Contains(problems, text => text.Contains(second.Name) && text.Contains("belt_conveyor_2m"));
        Assert.Contains(problems, text => text.Contains(third.Name) && text.Contains("pallet"));
    }
}
