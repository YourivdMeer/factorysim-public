using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class TagRecorderTests
{
    private static TagStore CreateStore() => new(new[]
    {
        new TagDefinition("At pusher", TagDirection.Sensor),
        new TagDefinition("Pusher", TagDirection.Actuator),
        new TagDefinition("Level", TagDirection.Sensor, TagKind.Int, Scale: 100, Unit: "m"),
    });

    private static void Tick(TagRecorder recorder, TagStore store, double time)
    {
        store.EndTick();
        recorder.Record(time, store);
    }

    [Fact]
    public void The_first_tick_stores_every_tag_once()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();

        Tick(recorder, store, 0.0);

        Assert.Equal(3, recorder.Count);
        Assert.Equal(new[] { "At pusher", "Pusher", "Level" }, recorder.Tags);
        Assert.Equal(0, recorder.ValueAt("Pusher", 0.0));
    }

    [Fact]
    public void Only_changes_are_stored()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);

        Tick(recorder, store, 0.1);
        Tick(recorder, store, 0.2);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 0.3);
        Tick(recorder, store, 0.4);

        Assert.Equal(4, recorder.Count);
        TagChange change = Assert.Single(recorder.Changes("At pusher", 0.1, 1.0));
        Assert.Equal(0.3, change.Time);
        Assert.Equal(1, change.Value);
    }

    [Fact]
    public void Value_at_a_change_and_between_two_changes()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);
        store.SetSensor("At pusher", false);
        Tick(recorder, store, 2.0);

        Assert.Equal(0, recorder.ValueAt("At pusher", 0.5));
        Assert.Equal(1, recorder.ValueAt("At pusher", 1.0));
        Assert.Equal(1, recorder.ValueAt("At pusher", 1.9));
        Assert.Equal(0, recorder.ValueAt("At pusher", 2.0));
        Assert.Equal(0, recorder.ValueAt("At pusher", 99.0));
        Assert.Null(recorder.ValueAt("At pusher", -1.0));
        Assert.Null(recorder.ValueAt("No such tag", 1.0));
    }

    [Fact]
    public void Rising_and_falling_edges()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);
        store.SetSensor("At pusher", false);
        Tick(recorder, store, 2.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 3.0);

        Assert.Equal(new[] { 1.0, 3.0 }, recorder.Edges("At pusher", rising: true));
        Assert.Equal(new[] { 2.0 }, recorder.Edges("At pusher", rising: false));
    }

    [Fact]
    public void A_change_of_force_is_a_change()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);

        store.Force("Pusher", false);
        Tick(recorder, store, 1.0);
        store.Force("Pusher", true);
        Tick(recorder, store, 2.0);
        store.Release("Pusher");
        Tick(recorder, store, 3.0);

        TagChange[] changes = recorder.Changes("Pusher", 0.5, 5.0).ToArray();
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, changes.Select(c => c.Time));
        Assert.Equal(new[] { 0, 1, 0 }, changes.Select(c => c.Value));
        Assert.Equal(new[] { true, true, false }, changes.Select(c => c.Forced));
        Assert.True(recorder.ForcedAt("Pusher", 1.5));
        Assert.False(recorder.ForcedAt("Pusher", 3.5));
    }

    [Fact]
    public void Analog_tags_store_their_number()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);
        store.SetSensorNumber("Level", 150);
        Tick(recorder, store, 1.0);

        Assert.Equal(150, recorder.ValueAt("Level", 1.5));
    }

    [Fact]
    public void Clear_stores_every_tag_again_on_the_next_tick()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);

        recorder.Clear();
        Assert.Equal(0, recorder.Count);
        Assert.Null(recorder.ValueAt("At pusher", 1.0));

        Tick(recorder, store, 0.0);
        Assert.Equal(3, recorder.Count);
        Assert.Equal(1, recorder.ValueAt("At pusher", 0.0));
    }

    [Fact]
    public void Paused_recording_stores_nothing()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);

        recorder.Paused = true;
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);
        Assert.Equal(3, recorder.Count);

        recorder.Paused = false;
        Tick(recorder, store, 2.0);
        Assert.Equal(new[] { 2.0 }, recorder.Edges("At pusher", rising: true));
    }

    [Fact]
    public void Changes_older_than_the_window_drop_off_but_the_state_on_the_border_stays()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder(window: 10.0);
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);
        store.SetSensor("At pusher", false);
        Tick(recorder, store, 2.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 3.0);

        Tick(recorder, store, 12.5);

        Assert.True(recorder.Origin >= 2.0 && recorder.Origin <= 2.5, $"origin {recorder.Origin}");
        Assert.Null(recorder.ValueAt("At pusher", 1.0));
        Assert.Equal(0, recorder.ValueAt("Pusher", recorder.Origin));
        Assert.Equal(1, recorder.ValueAt("At pusher", 5.0));
        Assert.Equal(new[] { 3.0 }, recorder.Edges("At pusher", rising: true));
    }

    [Fact]
    public void The_number_of_changes_is_capped()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder(window: 1000.0, maxChanges: 10);
        for (int i = 0; i < 50; i++)
        {
            store.SetSensor("At pusher", i % 2 == 1);
            Tick(recorder, store, i * 0.1);
        }

        Assert.True(recorder.Count <= 10, $"count {recorder.Count}");
        Assert.Equal(1, recorder.ValueAt("At pusher", 4.9));
        Assert.Equal(0, recorder.ValueAt("Pusher", 4.9));
        Assert.Equal(0, recorder.ValueAt("Level", recorder.Origin));
    }

    [Fact]
    public void Changes_include_the_state_on_the_border()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder(window: 10.0);
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.0);
        Tick(recorder, store, 20.0);

        TagChange first = recorder.Changes("At pusher", 0.0, 30.0).First();
        Assert.Equal(recorder.Origin, first.Time);
        Assert.Equal(1, first.Value);
    }

    [Fact]
    public void A_new_set_of_tags_starts_a_new_recording()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);

        var other = new TagStore(new[] { new TagDefinition("Lamp", TagDirection.Actuator) });
        Tick(recorder, other, 1.0);

        Assert.Equal(new[] { "Lamp" }, recorder.Tags);
        Assert.Equal(1, recorder.Count);
    }

    [Fact]
    public void Csv_lists_one_line_per_change_in_rising_time()
    {
        TagStore store = CreateStore();
        var recorder = new TagRecorder();
        Tick(recorder, store, 0.0);
        store.SetSensor("At pusher", true);
        Tick(recorder, store, 1.25);
        store.Force("Pusher", true);
        Tick(recorder, store, 1.5);
        store.SetSensorNumber("Level", 42);
        Tick(recorder, store, 2.0);

        string csv = recorder.ToCsv(new[] { "Pusher", "At pusher" }, 0.5, 10.0);

        string[] lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(new[]
        {
            "Time;Tag;Value;Forced",
            "1.250;At pusher;1;0",
            "1.500;Pusher;1;1",
        }, lines);
    }
}
