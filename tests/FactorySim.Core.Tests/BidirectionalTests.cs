using FactorySim.Core;

namespace FactorySim.Core.Tests;

/// <summary>Plan 12: a belt with a forward and a reverse output, and a pusher with a bistable valve.</summary>
public class BidirectionalTests
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

    [Theory]
    [InlineData(false, false, ConveyorDirection.Stopped, false)]
    [InlineData(true, false, ConveyorDirection.Forward, false)]
    [InlineData(false, true, ConveyorDirection.Backward, false)]
    [InlineData(true, true, ConveyorDirection.Stopped, true)]
    public void The_drive_follows_the_two_outputs_and_stops_on_a_conflict(bool forward, bool reverse, ConveyorDirection expected, bool conflict)
    {
        (ConveyorDirection direction, bool isConflict) = ConveyorDrive.Direction(forward, reverse);

        Assert.Equal(expected, direction);
        Assert.Equal(conflict, isConflict);
    }

    [Fact]
    public void A_bistable_pusher_goes_all_the_way_out_on_a_short_pulse_and_stays_there()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Bistable = true };

        pusher.ExtendCoil = true;
        Run(pusher, 0.2);
        pusher.ExtendCoil = false;
        Run(pusher, 2.0);

        Assert.True(pusher.AtFront);
        Assert.True(pusher.Extend);
        Assert.False(pusher.Conflict);
    }

    [Fact]
    public void A_bistable_pusher_goes_back_on_a_pulse_on_the_retract_coil()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Bistable = true };
        pusher.ExtendCoil = true;
        Run(pusher, 0.2);
        pusher.ExtendCoil = false;
        Run(pusher, 2.0);

        pusher.RetractCoil = true;
        Run(pusher, 0.2);
        pusher.RetractCoil = false;
        Run(pusher, 2.0);

        Assert.True(pusher.AtBack);
        Assert.False(pusher.Extend);
    }

    [Fact]
    public void With_both_coils_off_halfway_the_valve_keeps_its_position_and_the_pusher_does_not_spring_back()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Bistable = true };
        pusher.ExtendCoil = true;
        Run(pusher, 0.3);
        double halfway = pusher.Position;

        pusher.ExtendCoil = false;
        pusher.Tick(Dt);

        Assert.True(pusher.Position > halfway);
        Assert.True(pusher.Extend);
    }

    [Fact]
    public void Both_coils_on_is_a_conflict_that_keeps_the_last_command()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Bistable = true };
        Run(pusher, 0.5);

        pusher.ExtendCoil = true;
        pusher.RetractCoil = true;
        Run(pusher, 1.0);

        // The last command was "in": it does not move out.
        Assert.True(pusher.Conflict);
        Assert.True(pusher.AtBack);
        Assert.Equal(0.0, pusher.Position);

        pusher.RetractCoil = false;
        pusher.Tick(Dt);
        Assert.False(pusher.Conflict);
        Assert.True(pusher.Position > 0.0);
    }

    [Fact]
    public void Reset_puts_a_bistable_pusher_back_with_the_valve_on_retract()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Bistable = true };
        pusher.ExtendCoil = true;
        pusher.RetractCoil = true;
        Run(pusher, 0.1);
        pusher.ExtendCoil = true;
        pusher.RetractCoil = false;
        Run(pusher, 0.5);

        pusher.Reset();
        pusher.Tick(Dt);

        Assert.True(pusher.AtBack);
        Assert.False(pusher.Extend);
        Assert.False(pusher.ExtendCoil);
        Assert.False(pusher.RetractCoil);
        Assert.False(pusher.Conflict);
    }

    [Fact]
    public void A_monostable_pusher_ignores_the_coils_and_springs_back()
    {
        var pusher = new PusherLogic(stroke: 0.8, speed: 1.0) { Extend = true };
        Run(pusher, 0.3);

        pusher.Extend = false;
        Run(pusher, 1.0);

        Assert.True(pusher.AtBack);
        Assert.False(pusher.Conflict);
    }
}
