using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class TagStoreTests
{
    private static TagStore CreateStore() => new(new[]
    {
        new TagDefinition("Start", TagDirection.Sensor),
        new TagDefinition("At pusher", TagDirection.Sensor),
        new TagDefinition("Conveyor", TagDirection.Actuator),
        new TagDefinition("Pusher", TagDirection.Actuator),
    });

    [Fact]
    public void Names_are_listed_per_direction_in_definition_order()
    {
        TagStore store = CreateStore();

        Assert.Equal(new[] { "Start", "At pusher" }, store.SensorNames);
        Assert.Equal(new[] { "Conveyor", "Pusher" }, store.ActuatorNames);
    }

    [Fact]
    public void Sensor_value_reaches_the_link_only_after_the_tick_ends()
    {
        TagStore store = CreateStore();

        store.SetSensor("At pusher", true);
        Assert.Equal(new[] { false, false }, store.SnapshotSensors());

        store.EndTick();
        Assert.Equal(new[] { false, true }, store.SnapshotSensors());
    }

    [Fact]
    public void Actuator_value_reaches_the_simulation_only_after_the_tick_ends()
    {
        TagStore store = CreateStore();

        store.ApplyActuators(new[] { true, false });
        Assert.False(store.GetActuator("Conveyor"));

        store.EndTick();
        Assert.True(store.GetActuator("Conveyor"));
        Assert.False(store.GetActuator("Pusher"));
    }

    [Fact]
    public void Forced_sensor_overrides_what_the_link_sees()
    {
        TagStore store = CreateStore();
        store.SetSensor("Start", false);

        store.Force("Start", true);
        store.EndTick();
        Assert.True(store.SnapshotSensors()[0]);

        store.Release("Start");
        store.EndTick();
        Assert.False(store.SnapshotSensors()[0]);
    }

    [Fact]
    public void Forced_actuator_overrides_the_plc_but_keeps_the_plc_value()
    {
        TagStore store = CreateStore();
        store.ApplyActuators(new[] { false, false });

        store.Force("Conveyor", true);
        store.EndTick();

        Assert.True(store.GetActuator("Conveyor"));
        TagState state = store.Snapshot().Single(t => t.Name == "Conveyor");
        Assert.False(state.Value);
        Assert.True(state.Forced);
        Assert.True(state.EffectiveValue);

        store.ReleaseAll();
        store.EndTick();
        Assert.False(store.GetActuator("Conveyor"));
    }

    [Fact]
    public void Snapshot_lists_every_tag_with_its_direction()
    {
        TagStore store = CreateStore();

        IReadOnlyList<TagState> tags = store.Snapshot();

        Assert.Equal(4, tags.Count);
        Assert.Equal(TagDirection.Sensor, tags[0].Direction);
        Assert.Equal(TagDirection.Actuator, tags[3].Direction);
    }

    [Fact]
    public void Reset_clears_values_but_keeps_forces()
    {
        TagStore store = CreateStore();
        store.SetSensor("Start", true);
        store.ApplyActuators(new[] { true, true });
        store.Force("Pusher", true);
        store.EndTick();

        store.ResetValues();

        Assert.Equal(new[] { false, false }, store.SnapshotSensors());
        Assert.False(store.GetActuator("Conveyor"));
        Assert.True(store.GetActuator("Pusher"));
    }

    [Fact]
    public void Wrong_names_and_directions_are_rejected()
    {
        TagStore store = CreateStore();

        Assert.Throws<KeyNotFoundException>(() => store.SetSensor("Nope", true));
        Assert.Throws<InvalidOperationException>(() => store.SetSensor("Conveyor", true));
        Assert.Throws<InvalidOperationException>(() => store.GetActuator("Start"));
        Assert.Throws<ArgumentException>(() => store.ApplyActuators(new[] { true }));
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new TagStore(new[]
        {
            new TagDefinition("A", TagDirection.Sensor),
            new TagDefinition("A", TagDirection.Actuator),
        }));
    }

    [Fact]
    public void Link_thread_never_sees_a_half_written_tick()
    {
        TagStore store = CreateStore();
        using var stop = new CancellationTokenSource();
        int torn = 0;

        var reader = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                bool[] sensors = store.SnapshotSensors();
                if (sensors[0] != sensors[1])
                {
                    Interlocked.Increment(ref torn);
                }

                store.ApplyActuators(new[] { sensors[0], sensors[0] });
            }
        });
        reader.Start();

        // The simulation always writes both sensors to the same value within one tick.
        for (int i = 0; i < 50_000; i++)
        {
            bool value = (i & 1) == 0;
            store.SetSensor("Start", value);
            store.SetSensor("At pusher", value);
            store.EndTick();
            Assert.Equal(store.GetActuator("Conveyor"), store.GetActuator("Pusher"));
        }

        stop.Cancel();
        reader.Join();
        Assert.Equal(0, torn);
    }
}
