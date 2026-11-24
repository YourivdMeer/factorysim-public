using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class AnalogTagStoreTests
{
    private static TagStore CreateStore() => new(new[]
    {
        new TagDefinition("Start", TagDirection.Sensor),
        new TagDefinition("Level", TagDirection.Sensor, TagKind.Int, Scale: 100, Unit: "m", Min: 0, Max: 300),
        new TagDefinition("Conveyor", TagDirection.Actuator),
        new TagDefinition("Valve", TagDirection.Actuator, TagKind.Int, Unit: "%", Min: 0, Max: 100),
        new TagDefinition("Display", TagDirection.Actuator, TagKind.Int),
    });

    [Fact]
    public void Digital_and_analog_tags_are_listed_apart()
    {
        TagStore store = CreateStore();

        Assert.Equal(new[] { "Start" }, store.SensorNames);
        Assert.Equal(new[] { "Conveyor" }, store.ActuatorNames);
        Assert.Equal(new[] { "Level" }, store.AnalogSensorNames);
        Assert.Equal(new[] { "Valve", "Display" }, store.AnalogActuatorNames);
    }

    [Fact]
    public void Analog_sensor_reaches_the_link_only_after_the_tick_ends()
    {
        TagStore store = CreateStore();

        store.SetSensorNumber("Level", 120);
        Assert.Equal(new[] { 0 }, store.SnapshotSensorNumbers());
        store.EndTick();

        Assert.Equal(new[] { 120 }, store.SnapshotSensorNumbers());
    }

    [Fact]
    public void A_measured_value_is_scaled_to_a_whole_number()
    {
        TagStore store = CreateStore();

        store.SetSensorValue("Level", 1.234);
        store.EndTick();

        Assert.Equal(new[] { 123 }, store.SnapshotSensorNumbers());
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(301, 300)]
    [InlineData(40000, 300)]
    public void Analog_sensor_values_are_clipped_to_the_range(int written, int expected)
    {
        TagStore store = CreateStore();

        store.SetSensorNumber("Level", written);
        store.EndTick();

        Assert.Equal(new[] { expected }, store.SnapshotSensorNumbers());
    }

    [Fact]
    public void Without_a_range_values_are_clipped_to_a_16_bit_int()
    {
        TagStore store = CreateStore();

        store.ApplyActuatorNumbers(new[] { 0, 100000 });
        store.EndTick();

        Assert.Equal(short.MaxValue, store.GetActuatorNumber("Display"));
        store.ApplyActuatorNumbers(new[] { 0, -100000 });
        store.EndTick();
        Assert.Equal(short.MinValue, store.GetActuatorNumber("Display"));
    }

    [Fact]
    public void Analog_actuator_values_are_latched_at_the_end_of_the_tick_and_clipped()
    {
        TagStore store = CreateStore();

        store.ApplyActuatorNumbers(new[] { 150, -7 });
        Assert.Equal(0, store.GetActuatorNumber("Valve"));
        store.EndTick();

        Assert.Equal(100, store.GetActuatorNumber("Valve"));
        Assert.Equal(-7, store.GetActuatorNumber("Display"));
        Assert.Equal(100.0, store.GetActuatorValue("Valve"), 6);
    }

    [Fact]
    public void Scaled_value_goes_back_and_forth()
    {
        TagStore store = new(new[] { new TagDefinition("Level", TagDirection.Actuator, TagKind.Int, Scale: 100) });

        store.ApplyActuatorNumbers(new[] { 275 });
        store.EndTick();

        Assert.Equal(2.75, store.GetActuatorValue("Level"), 6);
    }

    [Fact]
    public void Forcing_an_analog_tag_overrides_both_sides_and_is_clipped()
    {
        TagStore store = CreateStore();
        store.SetSensorNumber("Level", 10);

        store.Force("Level", 999);
        store.Force("Valve", 40);
        store.ApplyActuatorNumbers(new[] { 90, 0 });
        store.EndTick();

        Assert.Equal(new[] { 300 }, store.SnapshotSensorNumbers());
        Assert.Equal(40, store.GetActuatorNumber("Valve"));
        TagState level = store.Snapshot().Single(t => t.Name == "Level");
        Assert.Equal(TagKind.Int, level.Kind);
        Assert.Equal(10, level.Number);
        Assert.True(level.Forced);
        Assert.Equal(300, level.EffectiveNumber);
        Assert.Equal("m", level.Unit);
        Assert.Equal(100, level.Scale);

        store.Release("Level");
        store.EndTick();
        Assert.Equal(new[] { 10 }, store.SnapshotSensorNumbers());
    }

    [Fact]
    public void The_link_takes_both_kinds_of_sensor_from_the_same_tick()
    {
        TagStore store = CreateStore();
        store.SetSensor("Start", true);
        store.SetSensorNumber("Level", 50);
        store.EndTick();

        (bool[] bits, int[] numbers) = store.SnapshotSensorImage();

        Assert.Equal(new[] { true }, bits);
        Assert.Equal(new[] { 50 }, numbers);
    }

    [Fact]
    public void Reset_clears_analog_values_but_keeps_forces()
    {
        TagStore store = CreateStore();
        store.SetSensorNumber("Level", 50);
        store.ApplyActuatorNumbers(new[] { 30, 4 });
        store.Force("Display", 9);
        store.EndTick();

        store.ResetValues();

        Assert.Equal(new[] { 0 }, store.SnapshotSensorNumbers());
        Assert.Equal(0, store.GetActuatorNumber("Valve"));
        Assert.Equal(9, store.GetActuatorNumber("Display"));
    }

    [Fact]
    public void A_digital_call_on_an_analog_tag_is_refused()
    {
        TagStore store = CreateStore();

        Assert.Throws<InvalidOperationException>(() => store.SetSensor("Level", true));
        Assert.Throws<InvalidOperationException>(() => store.GetActuator("Valve"));
        Assert.Throws<InvalidOperationException>(() => store.Force("Valve", true));
        Assert.Throws<InvalidOperationException>(() => store.SetSensorNumber("Start", 1));
        Assert.Throws<InvalidOperationException>(() => store.Force("Conveyor", 1));
    }

    [Fact]
    public void Wrong_number_of_analog_actuator_values_is_refused()
    {
        TagStore store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.ApplyActuatorNumbers(new[] { 1 }));
    }

    [Fact]
    public void A_range_upside_down_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new TagStore(new[]
        {
            new TagDefinition("X", TagDirection.Sensor, TagKind.Int, Min: 10, Max: 0),
        }));
    }
}
