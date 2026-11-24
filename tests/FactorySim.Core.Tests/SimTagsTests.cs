using FactorySim.Core;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

/// <summary>Plan 14: the optional simulation tags Sim paused and Sim reset.</summary>
public class SimTagsTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static Scene LoadMvp() =>
        Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));

    private static readonly (string Name, ModbusArea Area, int Offset)[] MvpTags =
    {
        ("Start button", ModbusArea.Coil, 0),
        ("Stop button", ModbusArea.Coil, 1),
        ("At pusher", ModbusArea.Coil, 2),
        ("High box", ModbusArea.Coil, 3),
        ("Pusher back", ModbusArea.Coil, 4),
        ("Pusher front", ModbusArea.Coil, 5),
        ("Sim running", ModbusArea.Coil, 6),
        ("Conveyor", ModbusArea.DiscreteInput, 0),
        ("Pusher", ModbusArea.DiscreteInput, 1),
        ("Emitter", ModbusArea.DiscreteInput, 2),
        ("Run lamp", ModbusArea.DiscreteInput, 3),
    };

    // ---- scene file

    [Fact]
    public void A_scene_without_the_field_has_no_sim_tags_and_does_not_write_it()
    {
        Scene scene = LoadMvp();

        Assert.Empty(scene.SimTags);
        Assert.DoesNotContain("simTags", scene.ToJson());
    }

    [Fact]
    public void Sim_tags_survive_a_round_trip()
    {
        Scene scene = LoadMvp();
        scene.SetSimTags(new[] { SceneTags.SimReset, SceneTags.SimPaused });

        Scene back = Scene.FromJson(scene.ToJson());

        Assert.Equal(new[] { SceneTags.SimPaused, SceneTags.SimReset }, back.SimTags);
        Assert.Contains("\"simTags\"", scene.ToJson());
    }

    [Fact]
    public void An_unknown_sim_tag_gives_a_clear_error()
    {
        Scene scene = LoadMvp();
        string json = scene.ToJson().Replace("\"tags\":", "\"simTags\": [\"Sim exploded\"],\n  \"tags\":");

        SceneException error = Assert.Throws<SceneException>(() => Scene.FromJson(json));
        Assert.Contains("Sim exploded", error.Message);
        Assert.Throws<SceneException>(() => scene.SetSimTags(new[] { "Sim running" }));
    }

    [Fact]
    public void Setting_a_sim_tag_twice_keeps_it_once()
    {
        Scene scene = LoadMvp();
        scene.SetSimTags(new[] { SceneTags.SimPaused, SceneTags.SimPaused });

        Assert.Equal(new[] { SceneTags.SimPaused }, scene.SimTags);
    }

    // ---- tags and addresses

    [Fact]
    public void Sim_tags_off_give_exactly_the_tags_and_addresses_of_the_acceptance_scene()
    {
        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(LoadMvp(), Catalog);

        Assert.Equal(MvpTags.OrderBy(t => t.Name), tags.Select(t => (t.Name, t.Area, t.Offset)).OrderBy(t => t.Name));
    }

    [Fact]
    public void Sim_tags_on_come_on_the_lowest_free_coils()
    {
        Scene scene = LoadMvp();
        scene.SetSimTags(new[] { SceneTags.SimPaused, SceneTags.SimReset });

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.Equal(MvpTags.Length + 2, tags.Count);
        TagMapEntry paused = Assert.Single(tags, t => t.Name == SceneTags.SimPaused);
        TagMapEntry reset = Assert.Single(tags, t => t.Name == SceneTags.SimReset);
        Assert.Equal((TagDirection.Sensor, ModbusArea.Coil, 7, "%IX0.7"), (paused.Direction, paused.Area, paused.Offset, paused.IecAddress));
        Assert.Equal((TagDirection.Sensor, ModbusArea.Coil, 8, "%IX1.0"), (reset.Direction, reset.Area, reset.Offset, reset.IecAddress));
        foreach ((string name, ModbusArea area, int offset) in MvpTags)
        {
            Assert.Contains(tags, t => t.Name == name && t.Area == area && t.Offset == offset);
        }
    }

    [Fact]
    public void Only_the_sim_tag_that_is_asked_for_is_added()
    {
        Scene scene = LoadMvp();
        scene.SetSimTags(new[] { SceneTags.SimReset });

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        Assert.DoesNotContain(tags, t => t.Name == SceneTags.SimPaused);
        Assert.Equal(7, Assert.Single(tags, t => t.Name == SceneTags.SimReset).Offset);
    }

    [Fact]
    public void A_saved_address_of_a_sim_tag_stays()
    {
        Scene scene = LoadMvp();
        scene.SetSimTags(new[] { SceneTags.SimPaused });
        scene.Tags.Add(new TagMapEntry(SceneTags.SimPaused, TagDirection.Sensor, ModbusArea.Coil, 12, "%IX1.4", "IO.iSimPaused"));

        TagMapEntry paused = Assert.Single(SceneTags.Build(scene, Catalog), t => t.Name == SceneTags.SimPaused);

        Assert.Equal(12, paused.Offset);
    }

    [Fact]
    public void No_part_can_take_the_name_of_a_sim_tag_even_when_they_are_off()
    {
        foreach (string name in new[] { SceneTags.SimPaused, SceneTags.SimReset })
        {
            Scene scene = new();
            SceneItem item = scene.Add(Catalog.Get("diffuse_sensor"), 0, 0);
            scene.Rename(item.Id, name);

            Assert.Contains(SceneTags.Problems(scene, Catalog), problem => problem.Contains(name, StringComparison.Ordinal));
            Assert.Throws<SceneException>(() => SceneTags.Build(scene, Catalog));
        }
    }

    // ---- reset pulse

    [Fact]
    public void Reset_pulse_starts_at_a_reset_and_lasts_one_second()
    {
        var pulse = new SimResetPulse();
        Assert.False(pulse.IsHigh(0.0));

        pulse.Trigger(10.0);

        Assert.True(pulse.IsHigh(10.0));
        Assert.True(pulse.IsHigh(10.99));
        Assert.False(pulse.IsHigh(11.0));
        Assert.False(pulse.IsHigh(20.0));
    }

    [Fact]
    public void A_second_reset_during_the_pulse_makes_it_last_one_second_again()
    {
        var pulse = new SimResetPulse();
        pulse.Trigger(10.0);
        pulse.Trigger(10.6);

        Assert.True(pulse.IsHigh(11.5));
        Assert.False(pulse.IsHigh(11.65));
    }
}
