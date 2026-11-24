using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class SimTimeScaleTests
{
    private const double Tick = 1.0 / 120.0;

    [Fact]
    public void Starts_at_scale_one_and_time_zero()
    {
        var sim = new SimController();

        Assert.Equal(1.0, sim.TimeScale);
        Assert.Equal(0.0, sim.SimTime);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void Accepts_the_fixed_scales(double scale)
    {
        var sim = new SimController();

        sim.SetTimeScale(scale);

        Assert.Equal(scale, sim.TimeScale);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(8.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Rejects_other_scales(double scale)
    {
        var sim = new SimController();

        Assert.Throws<ArgumentOutOfRangeException>(() => sim.SetTimeScale(scale));
        Assert.Equal(1.0, sim.TimeScale);
    }

    [Fact]
    public void Raises_the_event_only_on_a_change()
    {
        var sim = new SimController();
        var seen = new List<double>();
        sim.TimeScaleChanged += seen.Add;

        sim.SetTimeScale(1.0);
        sim.SetTimeScale(2.0);
        sim.SetTimeScale(2.0);

        Assert.Equal(new[] { 2.0 }, seen);
    }

    [Fact]
    public void Faster_and_slower_walk_the_list_and_stop_at_the_ends()
    {
        var sim = new SimController();

        sim.Faster();
        Assert.Equal(2.0, sim.TimeScale);
        sim.Faster();
        sim.Faster();
        Assert.Equal(4.0, sim.TimeScale);

        sim.SetTimeScale(1.0);
        sim.Slower();
        Assert.Equal(0.5, sim.TimeScale);
        sim.Slower();
        sim.Slower();
        Assert.Equal(0.25, sim.TimeScale);
    }

    [Fact]
    public void Sim_time_runs_only_while_running()
    {
        var sim = new SimController();

        sim.Advance(Tick);
        Assert.Equal(0.0, sim.SimTime);

        sim.Run();
        sim.Advance(Tick);
        sim.Advance(Tick);
        Assert.Equal(2 * Tick, sim.SimTime, 12);

        sim.Pause();
        sim.Advance(Tick);
        Assert.Equal(2 * Tick, sim.SimTime, 12);
    }

    [Fact]
    public void Step_outside_pause_does_nothing()
    {
        var sim = new SimController();

        sim.Step();
        Assert.False(sim.TakeStep());

        sim.Run();
        sim.Step();
        Assert.False(sim.TakeStep());
    }

    [Fact]
    public void A_step_is_taken_once_and_advances_one_tick()
    {
        var sim = new SimController();
        sim.Run();
        sim.Pause();

        sim.Step();

        Assert.True(sim.TakeStep());
        Assert.False(sim.TakeStep());
        sim.Advance(Tick);
        sim.Advance(Tick);                     // the world is paused again: this one does not count
        Assert.Equal(Tick, sim.SimTime, 12);
        Assert.Equal(SimState.Paused, sim.State);
    }

    [Fact]
    public void Reset_returns_scale_and_time()
    {
        var sim = new SimController();
        sim.SetTimeScale(4.0);
        sim.Run();
        sim.Advance(Tick);
        sim.Pause();
        sim.Step();

        sim.Reset();

        Assert.Equal(1.0, sim.TimeScale);
        Assert.Equal(0.0, sim.SimTime);
        Assert.False(sim.TakeStep());
    }

    [Fact]
    public void Warning_only_with_a_link_and_a_scale_other_than_one()
    {
        var sim = new SimController();

        Assert.Null(sim.TimeScaleWarning(linkConnected: false));
        Assert.Null(sim.TimeScaleWarning(linkConnected: true));

        sim.SetTimeScale(4.0);
        Assert.Null(sim.TimeScaleWarning(linkConnected: false));
        Assert.Equal("Externe PLC loopt op echte tijd: timers kloppen niet bij ×4", sim.TimeScaleWarning(linkConnected: true));

        sim.SetTimeScale(0.25);
        Assert.Equal("Externe PLC loopt op echte tijd: timers kloppen niet bij ×0,25", sim.TimeScaleWarning(linkConnected: true));
    }

    [Theory]
    [InlineData(0.25, "×0,25")]
    [InlineData(0.5, "×0,5")]
    [InlineData(1.0, "×1")]
    [InlineData(4.0, "×4")]
    public void Labels_use_a_decimal_comma(double scale, string label)
    {
        Assert.Equal(label, SimController.ScaleLabel(scale));
    }
}
