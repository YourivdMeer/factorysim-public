namespace FactorySim.Core.Tests;

public class PanelAnalogTests
{
    [Fact]
    public void A_new_knob_is_at_zero_and_turned_fully_left()
    {
        var knob = new KnobLogic();

        Assert.Equal(0, knob.Value);
        Assert.Equal(-135.0, knob.AngleDegrees, 6);
    }

    [Fact]
    public void A_step_turns_the_knob_by_a_twentieth()
    {
        var knob = new KnobLogic();

        knob.Step(+1);
        knob.Step(+1);

        Assert.Equal(100, knob.Value);
        knob.Step(-1);
        Assert.Equal(50, knob.Value);
    }

    [Fact]
    public void The_knob_stops_at_both_ends()
    {
        var knob = new KnobLogic();

        knob.Step(-3);
        Assert.Equal(0, knob.Value);
        knob.Step(+100);
        Assert.Equal(KnobLogic.Maximum, knob.Value);
        Assert.Equal(135.0, knob.AngleDegrees, 6);
    }

    [Fact]
    public void Halfway_points_straight_up()
    {
        var knob = new KnobLogic();

        knob.Set(500);

        Assert.Equal(0.0, knob.AngleDegrees, 6);
    }

    [Fact]
    public void Set_clips_and_reset_returns_to_the_start_value()
    {
        var knob = new KnobLogic(start: 200);

        knob.Set(5000);
        Assert.Equal(1000, knob.Value);
        knob.Reset();
        Assert.Equal(200, knob.Value);
    }

    [Theory]
    [InlineData(1234, 0, "1234")]
    [InlineData(1234, 2, "12,34")]
    [InlineData(5, 2, "0,05")]
    [InlineData(-5, 1, "-0,5")]
    [InlineData(-32768, 0, "-32768")]
    [InlineData(7, 3, "0,007")]
    public void The_display_shows_the_number_with_its_decimals(int number, int decimals, string expected)
    {
        Assert.Equal(expected, DisplayText.Format(number, decimals));
    }

    [Fact]
    public void Decimals_outside_zero_to_four_are_clipped()
    {
        Assert.Equal("1,2345", DisplayText.Format(12345, 9));
        Assert.Equal("12345", DisplayText.Format(12345, -1));
    }
}
