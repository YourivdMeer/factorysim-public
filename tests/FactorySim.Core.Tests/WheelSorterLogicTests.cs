using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class WheelSorterLogicTests
{
    [Fact]
    public void Nothing_commanded_means_the_deck_stands_still()
    {
        Assert.Equal((0.0, 0.0), WheelSorterLogic.Direction(run: false, left: false, right: false, angleDegrees: 45.0));
    }

    [Fact]
    public void Run_alone_carries_straight_on()
    {
        Assert.Equal((1.0, 0.0), WheelSorterLogic.Direction(run: true, left: false, right: false, angleDegrees: 45.0));
    }

    [Theory]
    [InlineData(true, false, 1.0)]
    [InlineData(false, true, -1.0)]
    public void A_side_command_steers_at_the_wheel_angle_with_or_without_run(bool left, bool right, double sign)
    {
        foreach (bool run in new[] { false, true })
        {
            (double forward, double toLeft) = WheelSorterLogic.Direction(run, left, right, angleDegrees: 30.0);

            Assert.Equal(Math.Cos(Math.PI / 6.0), forward, 9);
            Assert.Equal(sign * 0.5, toLeft, 9);
        }
    }

    [Fact]
    public void At_ninety_degrees_a_side_command_carries_square_off_with_no_forward_motion()
    {
        (double forward, double toLeft) = WheelSorterLogic.Direction(run: true, left: false, right: true, angleDegrees: 90.0);

        Assert.Equal(0.0, forward, 9);
        Assert.Equal(-1.0, toLeft, 9);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, -1)]
    [InlineData(true, true, 0)]
    public void Rollers_run_forward_or_in_reverse_and_stand_still_on_both(bool forward, bool reverse, int expected)
    {
        Assert.Equal(expected, WheelSorterLogic.Roll(forward, reverse));
    }

    [Fact]
    public void Both_sides_at_once_cancel_out()
    {
        Assert.Equal((1.0, 0.0), WheelSorterLogic.Direction(run: true, left: true, right: true, angleDegrees: 45.0));
        Assert.Equal((0.0, 0.0), WheelSorterLogic.Direction(run: false, left: true, right: true, angleDegrees: 45.0));
    }

    [Fact]
    public void The_wheels_are_up_only_while_exactly_one_side_is_commanded()
    {
        Assert.True(WheelSorterLogic.WheelsUp(left: true, right: false));
        Assert.True(WheelSorterLogic.WheelsUp(left: false, right: true));
        Assert.False(WheelSorterLogic.WheelsUp(left: false, right: false));
        Assert.False(WheelSorterLogic.WheelsUp(left: true, right: true));
    }
}
