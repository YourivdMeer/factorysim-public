namespace FactorySim.Core.Tests;

public class ForkAxisTests
{
    [Fact]
    public void Rests_in_the_middle()
    {
        var forks = new ForkAxis(stroke: 1.4, speed: 0.7);

        forks.Tick(1.0);

        Assert.Equal(0.0, forks.Position, 6);
        Assert.True(forks.AtMiddle);
        Assert.False(forks.AtLeftEnd);
        Assert.False(forks.AtRightEnd);
    }

    [Fact]
    public void Left_goes_negative_and_right_goes_positive()
    {
        var forks = new ForkAxis(1.4, 0.7) { Left = true };
        forks.Tick(1.0);
        Assert.Equal(-0.7, forks.Position, 6);
        Assert.False(forks.AtMiddle);

        forks.Left = false;
        forks.Right = true;
        forks.Tick(1.0);
        Assert.Equal(0.0, forks.Position, 6);
        forks.Tick(1.0);
        Assert.Equal(0.7, forks.Position, 6);
    }

    [Fact]
    public void Stops_at_the_end_and_reports_it()
    {
        var forks = new ForkAxis(1.4, 0.7) { Left = true };
        forks.Tick(5.0);

        Assert.Equal(-1.4, forks.Position, 6);
        Assert.True(forks.AtLeftEnd);
        Assert.False(forks.AtRightEnd);

        forks.Left = false;
        forks.Right = true;
        forks.Tick(10.0);
        Assert.True(forks.AtRightEnd);
    }

    [Fact]
    public void Without_a_command_it_returns_to_the_middle()
    {
        var forks = new ForkAxis(1.4, 0.7) { Left = true };
        forks.Tick(1.0);
        forks.Left = false;

        forks.Tick(0.5);
        Assert.Equal(-0.35, forks.Position, 6);

        forks.Tick(5.0);
        Assert.True(forks.AtMiddle);
    }

    [Fact]
    public void Both_directions_at_once_do_nothing()
    {
        var forks = new ForkAxis(1.4, 0.7) { Left = true };
        forks.Tick(1.0);

        forks.Right = true;
        forks.Tick(1.0);

        Assert.Equal(-0.7, forks.Position, 6);
        Assert.False(forks.Moving);
    }
}
