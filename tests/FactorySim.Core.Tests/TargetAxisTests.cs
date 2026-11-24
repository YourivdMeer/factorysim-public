namespace FactorySim.Core.Tests;

public class TargetAxisTests
{
    [Fact]
    public void Stands_still_at_its_start_without_a_target()
    {
        var axis = new TargetAxis(min: 0.0, max: 4.0, speed: 0.5, start: 1.0);

        axis.Tick(1.0);

        Assert.Equal(1.0, axis.Position, 6);
        Assert.False(axis.Moving);
        Assert.False(axis.Pending);
    }

    [Fact]
    public void Runs_to_a_target_at_constant_speed_and_stops_on_it()
    {
        var axis = new TargetAxis(0.0, 4.0, 0.5, 1.0);
        axis.MoveTo(2.0);

        axis.Tick(0.5);
        Assert.Equal(1.25, axis.Position, 6);
        Assert.True(axis.Moving);
        Assert.True(axis.Pending);

        axis.Tick(5.0);
        Assert.Equal(2.0, axis.Position, 6);
        Assert.False(axis.Pending);

        axis.Tick(1.0);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Runs_downwards_too()
    {
        var axis = new TargetAxis(0.0, 4.0, 0.5, 3.0);
        axis.MoveTo(1.0);

        axis.Tick(2.0);

        Assert.Equal(2.0, axis.Position, 6);
    }

    [Fact]
    public void A_target_outside_the_travel_is_clamped()
    {
        var axis = new TargetAxis(0.0, 4.0, 10.0, 1.0);
        axis.MoveTo(9.0);
        axis.Tick(1.0);

        Assert.Equal(4.0, axis.Position, 6);
    }

    [Fact]
    public void A_blocked_axis_keeps_its_target_and_waits()
    {
        var axis = new TargetAxis(0.0, 4.0, 0.5, 1.0);
        axis.MoveTo(2.0);

        axis.Tick(1.0, enabled: false);
        Assert.Equal(1.0, axis.Position, 6);
        Assert.False(axis.Moving);
        Assert.True(axis.Pending);

        axis.Tick(1.0);
        Assert.Equal(1.5, axis.Position, 6);
    }

    [Fact]
    public void Jogs_while_a_direction_is_commanded_and_stops_at_the_ends()
    {
        var axis = new TargetAxis(0.0, 1.0, 0.5, 0.5) { Jog = 1 };

        axis.Tick(0.4);
        Assert.Equal(0.7, axis.Position, 6);

        axis.Tick(5.0);
        Assert.Equal(1.0, axis.Position, 6);

        axis.Jog = -1;
        axis.Tick(0.2);
        Assert.Equal(0.9, axis.Position, 6);

        axis.Jog = 0;
        axis.Tick(1.0);
        Assert.Equal(0.9, axis.Position, 6);
    }

    [Fact]
    public void Reset_goes_back_to_the_start_and_forgets_the_target()
    {
        var axis = new TargetAxis(0.0, 4.0, 0.5, 1.0);
        axis.MoveTo(3.0);
        axis.Tick(1.0);

        axis.Reset();

        Assert.Equal(1.0, axis.Position, 6);
        Assert.False(axis.Pending);
    }
}
