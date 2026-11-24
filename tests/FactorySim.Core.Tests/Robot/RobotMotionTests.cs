using FactorySim.Core.Robot;

namespace FactorySim.Core.Tests.Robot;

public class RobotMotionTests
{
    private const double Dt = 0.01;

    private static readonly RobotGeometry Geometry = RobotGeometry.Default;

    [Fact]
    public void A_new_motion_stands_still_at_home()
    {
        var motion = new RobotMotion(Geometry, speedPercent: 40.0);

        Assert.False(motion.Busy);
        Assert.Equal(RobotAngles.Home, motion.Angles);
    }

    [Fact]
    public void All_axes_arrive_at_the_same_time()
    {
        var motion = new RobotMotion(Geometry, speedPercent: 100.0);
        var target = new RobotAngles(100.0, 20.0, -10.0, 160.0, 5.0, -300.0);
        motion.MoveTo(target);

        // Axis 6 is the slowest here: 300 degrees at 420 degrees per second.
        double expected = 300.0 / 420.0;
        int ticks = 0;
        RobotAngles half = default;
        while (motion.Busy && ticks < 10000)
        {
            motion.Tick(Dt);
            ticks++;
            if (ticks == (int)Math.Round(expected / 2.0 / Dt))
            {
                half = motion.Angles;
            }
        }

        Assert.Equal(expected, ticks * Dt, 1);
        Assert.Equal(target, motion.Angles);
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            Assert.True(Math.Abs(half[axis] - target[axis] / 2.0) <= Math.Abs(target[axis]) * 0.02 + 1e-9,
                $"axis {axis + 1} at {half[axis]} halfway");
        }
    }

    [Fact]
    public void No_axis_goes_faster_than_its_maximum_and_nothing_jumps()
    {
        var motion = new RobotMotion(Geometry, speedPercent: 40.0);
        motion.MoveTo(new RobotAngles(-150.0, 100.0, -100.0, 150.0, -110.0, 390.0));

        RobotAngles previous = motion.Angles;
        while (motion.Busy)
        {
            motion.Tick(Dt);
            RobotAngles now = motion.Angles;
            for (int axis = 0; axis < RobotAngles.Count; axis++)
            {
                double speed = Math.Abs(now[axis] - previous[axis]) / Dt;
                Assert.True(speed <= Geometry.MaxSpeed[axis] * 0.4 + 1e-6, $"axis {axis + 1} at {speed} deg/s");
            }

            previous = now;
        }
    }

    [Fact]
    public void Speed_is_clamped_between_5_and_100_percent()
    {
        Assert.Equal(5.0, new RobotMotion(Geometry, 1.0).SpeedPercent);
        Assert.Equal(100.0, new RobotMotion(Geometry, 250.0).SpeedPercent);
    }

    [Fact]
    public void Jump_puts_the_arm_at_once_and_stops_a_motion()
    {
        var motion = new RobotMotion(Geometry, 40.0);
        motion.MoveTo(new RobotAngles(90, 0, 0, 0, 0, 0));
        motion.Tick(Dt);
        var pose = new RobotAngles(10, 20, 30, 0, 0, 0);

        motion.Jump(pose);

        Assert.False(motion.Busy);
        Assert.Equal(pose, motion.Angles);
    }

    [Fact]
    public void A_target_outside_the_axis_ranges_is_clamped_to_them()
    {
        var motion = new RobotMotion(Geometry, 100.0);
        motion.MoveTo(new RobotAngles(500, 0, 0, 0, 0, 0));
        while (motion.Busy)
        {
            motion.Tick(Dt);
        }

        Assert.Equal(Geometry.Max[0], motion.Angles.A1);
    }
}
