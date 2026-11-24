namespace FactorySim.Core.Tests;

public class StackerPositionsTests
{
    private static StackerPositions Layout() => new(
        columnSpacing: 1.5, levelHeight: 1.2, firstLevelTop: 0.3, transferTop: 0.5, clearance: 0.03);

    [Theory]
    [InlineData(0, -1.5, 0.47)]     // in: column 1 at the height of the roller conveyor
    [InlineData(1, -1.5, 0.27)]
    [InlineData(2, 0.0, 0.27)]
    [InlineData(3, 1.5, 0.27)]
    [InlineData(4, -1.5, 1.47)]
    [InlineData(5, 0.0, 1.47)]
    [InlineData(6, 1.5, 1.47)]
    [InlineData(7, -1.5, 2.67)]
    [InlineData(9, 1.5, 2.67)]
    [InlineData(10, -1.5, 3.87)]
    [InlineData(12, 1.5, 3.87)]
    [InlineData(13, 1.5, 0.47)]     // out: column 3 at the height of the roller conveyor
    public void Every_position_has_an_x_and_a_height(int number, double x, double height)
    {
        Assert.True(Layout().TryGet(number, out double actualX, out double actualHeight));
        Assert.Equal(x, actualX, 6);
        Assert.Equal(height, actualHeight, 6);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void A_number_without_a_position_gives_nothing(int number)
    {
        Assert.False(Layout().TryGet(number, out _, out _));
    }

    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(true, false, false, false, 1)]
    [InlineData(true, false, true, false, 5)]
    [InlineData(false, false, false, true, 8)]
    [InlineData(false, true, false, true, 10)]
    [InlineData(true, true, true, true, 15)]
    public void Four_bits_make_a_binary_number(bool b1, bool b2, bool b4, bool b8, int number)
    {
        Assert.Equal(number, StackerPositions.FromBits(b1, b2, b4, b8));
    }

    [Fact]
    public void Columns_and_levels_are_known_separately_for_the_jog_sensors()
    {
        StackerPositions layout = Layout();

        Assert.Equal(-1.5, layout.ColumnX(1), 6);
        Assert.Equal(0.0, layout.ColumnX(2), 6);
        Assert.Equal(1.5, layout.ColumnX(3), 6);
        Assert.Equal(0.47, layout.LevelHeight(0), 6);
        Assert.Equal(0.27, layout.LevelHeight(1), 6);
        Assert.Equal(3.87, layout.LevelHeight(4), 6);
    }

    [Fact]
    public void The_travel_range_covers_every_position()
    {
        StackerPositions layout = Layout();

        for (int number = 0; number <= 13; number++)
        {
            layout.TryGet(number, out double x, out double height);
            Assert.InRange(x, layout.MinX, layout.MaxX);
            Assert.InRange(height, layout.MinHeight, layout.MaxHeight);
        }
    }
}
