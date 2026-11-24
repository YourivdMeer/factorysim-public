using Xunit;

namespace FactorySim.Core.Tests;

public class PositioningClampTests
{
    private const double OpenHalfGap = 0.52;      // distance of each blade's inner face from the centre line when open
    private const double Stroke = 0.42;
    private const double Speed = 0.3;

    private static PositioningClamp Clamp() => new(OpenHalfGap, Stroke, Speed);

    private static void Run(PositioningClamp clamp, double seconds, double? itemHalfWidth = null)
    {
        for (int i = 0; i < (int)(seconds * 120); i++)
        {
            clamp.Tick(1.0 / 120.0, itemHalfWidth);
        }
    }

    [Fact]
    public void At_rest_the_clamps_are_open_and_not_closed()
    {
        PositioningClamp clamp = Clamp();

        Assert.True(clamp.Open);
        Assert.False(clamp.Closed);
        Assert.Equal(0.0, clamp.Position);
        Assert.Equal(OpenHalfGap, clamp.HalfGap);
    }

    [Fact]
    public void Without_an_item_the_clamps_close_all_the_way_and_then_report_closed()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;

        Run(clamp, Stroke / Speed / 2.0);
        Assert.False(clamp.Open);
        Assert.False(clamp.Closed);                          // still on their way

        Run(clamp, Stroke / Speed);                          // well past the end
        Assert.True(clamp.Closed);
        Assert.False(clamp.Open);
        Assert.Equal(Stroke, clamp.Position, 6);
        Assert.Equal(OpenHalfGap - Stroke, clamp.HalfGap, 6);
    }

    [Fact]
    public void Against_an_item_the_clamps_stop_on_it_and_report_closed()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;

        Run(clamp, 3.0, itemHalfWidth: 0.15);

        Assert.True(clamp.Closed);
        Assert.Equal(0.15, clamp.HalfGap, 6);                // both blades touch a 0.30 m item
        Assert.Equal(OpenHalfGap - 0.15, clamp.Position, 6);
    }

    [Fact]
    public void Letting_go_opens_the_clamps_again_whatever_was_between_them()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;
        Run(clamp, 3.0, itemHalfWidth: 0.15);

        clamp.Close = false;
        Run(clamp, 0.2, itemHalfWidth: 0.15);
        Assert.False(clamp.Closed);
        Assert.False(clamp.Open);

        Run(clamp, 3.0);
        Assert.True(clamp.Open);
        Assert.False(clamp.Closed);
        Assert.Equal(0.0, clamp.Position);
    }

    [Fact]
    public void An_item_that_appears_after_the_clamps_passed_its_width_does_not_push_them_back()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;
        Run(clamp, 0.7);                                       // 0.21 m closed: half gap 0.31

        double before = clamp.Position;
        Run(clamp, 0.2, itemHalfWidth: 0.4);                   // limit would be 0.12 m: behind them

        Assert.Equal(before, clamp.Position, 6);
        Assert.True(clamp.Closed);
    }

    [Fact]
    public void An_item_that_is_removed_while_clamped_lets_the_clamps_carry_on_closing()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;
        Run(clamp, 3.0, itemHalfWidth: 0.15);
        Assert.True(clamp.Closed);

        Run(clamp, 0.1);                                       // item gone

        Assert.False(clamp.Closed);                            // on their way again
        Run(clamp, 2.0);
        Assert.Equal(Stroke, clamp.Position, 6);
        Assert.True(clamp.Closed);
    }

    [Fact]
    public void Reset_opens_the_clamps_at_once()
    {
        PositioningClamp clamp = Clamp();
        clamp.Close = true;
        Run(clamp, 3.0);

        clamp.Reset();

        Assert.True(clamp.Open);
        Assert.False(clamp.Closed);
        Assert.False(clamp.Close);
    }

    [Theory]
    [InlineData(0.0, 0.15)]                                    // square on: half of the 0.30 m side
    [InlineData(90.0, 0.15)]
    [InlineData(45.0, 0.2121320)]                              // turned: the diagonal reaches further
    public void The_half_width_of_an_item_across_the_clamps_follows_its_turn(double degrees, double expected)
    {
        double angle = degrees * Math.PI / 180.0;

        // The clamp axis seen in the item's own frame, for an item turned about the vertical axis.
        double half = PositioningClamp.HalfExtent(0.30, 0.20, 0.30, Math.Sin(angle), 0.0, Math.Cos(angle));

        Assert.Equal(expected, half, 6);
    }

    [Fact]
    public void The_blades_must_not_meet_before_the_end_of_the_stroke_and_the_numbers_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositioningClamp(0.3, 0.3, Speed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositioningClamp(OpenHalfGap, 0.0, Speed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositioningClamp(OpenHalfGap, Stroke, 0.0));
    }
}
