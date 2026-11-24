using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class LinearAxisTests
{
    [Fact]
    public void Extends_at_its_speed_and_reports_moving_until_it_arrives()
    {
        var axis = new LinearAxis(stroke: 1.0, speed: 2.0) { Extend = true };

        axis.Tick(0.25);
        Assert.Equal(0.5, axis.Position, 9);
        Assert.True(axis.Moving);

        axis.Tick(0.25);
        Assert.Equal(1.0, axis.Position, 9);
        Assert.True(axis.Moving);

        axis.Tick(0.25);
        Assert.Equal(1.0, axis.Position, 9);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Retracts_when_the_command_drops()
    {
        var axis = new LinearAxis(stroke: 1.0, speed: 2.0) { Extend = true };
        axis.Tick(1.0);

        axis.Extend = false;
        axis.Tick(0.25);

        Assert.Equal(0.5, axis.Position, 9);
        Assert.True(axis.Moving);
    }

    [Fact]
    public void An_obstacle_stops_the_axis_short_of_its_stroke()
    {
        var axis = new LinearAxis(stroke: 1.0, speed: 2.0) { Extend = true };

        axis.Tick(0.25, limit: 0.3);
        Assert.Equal(0.3, axis.Position, 9);
        Assert.True(axis.Moving);

        axis.Tick(0.25, limit: 0.3);
        Assert.Equal(0.3, axis.Position, 9);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void An_obstacle_never_pushes_the_axis_back_and_does_not_hinder_retracting()
    {
        var axis = new LinearAxis(stroke: 1.0, speed: 2.0) { Extend = true };
        axis.Tick(0.25);

        axis.Tick(0.25, limit: 0.2);
        Assert.Equal(0.5, axis.Position, 9);

        axis.Extend = false;
        axis.Tick(0.1, limit: 0.2);
        Assert.Equal(0.3, axis.Position, 9);
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        var axis = new LinearAxis(stroke: 1.0, speed: 2.0) { Extend = true };
        axis.Tick(0.25);

        axis.Reset();

        Assert.Equal(0.0, axis.Position);
        Assert.False(axis.Extend);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Stroke_and_speed_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearAxis(0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearAxis(1.0, 0.0));
    }
}
