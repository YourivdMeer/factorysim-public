using Xunit;

namespace FactorySim.Core.Tests;

public class ConveyorSlopeTests
{
    [Fact]
    public void A_flat_belt_has_no_angle_and_its_surface_is_as_long_as_the_belt()
    {
        var slope = new ConveyorSlope(Length: 2.0, Rise: 0.0);

        Assert.Equal(0.0, slope.Angle);
        Assert.Equal(2.0, slope.SurfaceLength);
        Assert.Equal((1.0, 0.0), slope.Direction(reverse: false));
        Assert.Equal((0.0, 0.0), slope.ColliderOffset(0.2));
    }

    [Fact]
    public void A_belt_of_four_metres_that_climbs_one_metre_rises_fourteen_degrees()
    {
        var slope = new ConveyorSlope(Length: 4.0, Rise: 1.0);

        Assert.Equal(14.036, slope.Angle * 180.0 / Math.PI, 3);
        Assert.Equal(Math.Sqrt(17.0), slope.SurfaceLength, 9);
    }

    [Fact]
    public void The_travel_direction_follows_the_surface_and_reversing_flips_both_components()
    {
        var slope = new ConveyorSlope(Length: 4.0, Rise: 1.0);

        (double x, double y) = slope.Direction(reverse: false);
        Assert.Equal(4.0 / Math.Sqrt(17.0), x, 9);
        Assert.Equal(1.0 / Math.Sqrt(17.0), y, 9);
        Assert.Equal(1.0, Math.Sqrt(x * x + y * y), 9);

        (double rx, double ry) = slope.Direction(reverse: true);
        Assert.Equal((-x, -y), (rx, ry));
    }

    [Fact]
    public void The_collider_hangs_below_the_surface_along_the_normal_of_the_slope()
    {
        var slope = new ConveyorSlope(Length: 4.0, Rise: 1.0);
        const double thickness = 0.2;

        (double x, double y) = slope.ColliderOffset(thickness);

        // The node origin is half a thickness straight below the surface point. The collider centre is
        // half a thickness below that point along the normal (-sin, cos), so its top face passes through it.
        // Seen from the node origin: up by half a thickness, then down along the normal.
        double half = thickness / 2.0;
        Assert.Equal(half * Math.Sin(slope.Angle), x, 9);
        Assert.Equal(half - half * Math.Cos(slope.Angle), y, 9);
        Assert.True(x > 0.0 && y > 0.0);
    }
}
