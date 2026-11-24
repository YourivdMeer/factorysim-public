using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class PusherLogicTests
{
    private const double Dt = 1.0 / 120.0;

    private static void Run(PusherLogic pusher, double seconds)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            pusher.Tick(Dt);
        }
    }

    [Fact]
    public void Starts_retracted_at_the_back_limit()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0);

        Assert.Equal(0.0, pusher.Position);
        Assert.True(pusher.AtBack);
        Assert.False(pusher.AtFront);
    }

    [Fact]
    public void Extends_at_the_configured_speed()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };

        Run(pusher, 0.4);

        Assert.Equal(0.4, pusher.Position, precision: 3);
        Assert.False(pusher.AtBack);
        Assert.False(pusher.AtFront);
    }

    [Fact]
    public void Stops_at_the_front_limit()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };

        Run(pusher, 5.0);

        Assert.Equal(0.8, pusher.Position, precision: 9);
        Assert.True(pusher.AtFront);
        Assert.False(pusher.AtBack);
    }

    [Fact]
    public void Retracts_when_the_command_drops()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };
        Run(pusher, 5.0);

        pusher.Extend = false;
        Run(pusher, 5.0);

        Assert.Equal(0.0, pusher.Position, precision: 9);
        Assert.True(pusher.AtBack);
    }

    [Fact]
    public void Reverses_mid_stroke_without_jumping()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };
        Run(pusher, 0.4);

        pusher.Extend = false;
        double before = pusher.Position;
        pusher.Tick(Dt);

        Assert.Equal(before - Dt, pusher.Position, precision: 9);
    }

    [Fact]
    public void Velocity_reports_the_actual_motion()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0);
        pusher.Tick(Dt);
        Assert.Equal(0.0, pusher.Velocity);

        pusher.Extend = true;
        pusher.Tick(Dt);
        Assert.Equal(1.0, pusher.Velocity, precision: 6);

        Run(pusher, 5.0);
        Assert.Equal(0.0, pusher.Velocity);
    }

    [Fact]
    public void Reset_returns_to_the_back_position()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };
        Run(pusher, 0.5);

        pusher.Reset();

        Assert.Equal(0.0, pusher.Position);
        Assert.False(pusher.Extend);
        Assert.True(pusher.AtBack);
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PusherLogic(stroke: 0.0, speed: 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PusherLogic(stroke: 0.8, speed: 0.0));
    }
}
