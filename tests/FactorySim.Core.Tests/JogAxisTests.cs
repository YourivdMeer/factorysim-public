namespace FactorySim.Core.Tests;

public class JogAxisTests
{
    [Fact]
    public void Starts_at_the_bottom_and_stands_still()
    {
        var axis = new JogAxis(travel: 1.0, speed: 0.5);

        axis.Tick(1.0);

        Assert.Equal(0.0, axis.Position, 6);
        Assert.True(axis.AtMin);
        Assert.False(axis.AtMax);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Moves_only_while_a_direction_is_commanded()
    {
        var axis = new JogAxis(travel: 1.0, speed: 0.5) { Up = true };

        axis.Tick(0.4);
        Assert.Equal(0.2, axis.Position, 6);
        Assert.True(axis.Moving);
        Assert.False(axis.AtMin);

        axis.Up = false;
        axis.Tick(0.4);
        Assert.Equal(0.2, axis.Position, 6);
        Assert.False(axis.Moving);

        axis.Down = true;
        axis.Tick(0.2);
        Assert.Equal(0.1, axis.Position, 6);
        Assert.True(axis.Moving);
    }

    [Fact]
    public void Stops_at_both_ends_of_its_travel()
    {
        var axis = new JogAxis(travel: 1.0, speed: 0.5) { Up = true };

        axis.Tick(5.0);
        axis.Tick(0.1);
        Assert.Equal(1.0, axis.Position, 6);
        Assert.True(axis.AtMax);
        Assert.False(axis.Moving);

        axis.Up = false;
        axis.Down = true;
        axis.Tick(5.0);
        axis.Tick(0.1);
        Assert.Equal(0.0, axis.Position, 6);
        Assert.True(axis.AtMin);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Both_directions_at_once_do_nothing()
    {
        var axis = new JogAxis(travel: 1.0, speed: 0.5) { Up = true, Down = true };

        axis.Tick(1.0);

        Assert.Equal(0.0, axis.Position, 6);
        Assert.False(axis.Moving);
    }

    [Fact]
    public void Reset_returns_to_the_bottom_with_no_command()
    {
        var axis = new JogAxis(travel: 1.0, speed: 0.5) { Up = true };
        axis.Tick(1.0);

        axis.Reset();

        Assert.Equal(0.0, axis.Position, 6);
        Assert.False(axis.Up);
        Assert.False(axis.Moving);
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 0.0)]
    public void Rejects_a_travel_or_speed_that_is_not_positive(double travel, double speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JogAxis(travel, speed));
    }
}
