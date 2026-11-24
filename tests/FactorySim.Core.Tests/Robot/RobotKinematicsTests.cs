using FactorySim.Core.Robot;

namespace FactorySim.Core.Tests.Robot;

public class RobotKinematicsTests
{
    private const double Mm = 0.001;

    private static readonly RobotGeometry Geometry = RobotGeometry.Default;

    private static void Near(Vector3d expected, Vector3d actual, double tolerance = Mm)
    {
        Assert.True((expected - actual).Length <= tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void The_default_geometry_has_the_published_reach()
    {
        Assert.Equal(0.580, Geometry.Reach, 3);
    }

    [Fact]
    public void At_home_the_tool_points_straight_down_under_the_wrist()
    {
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home);

        double wristHeight = Geometry.BaseHeight + Geometry.UpperArm + Geometry.ElbowOffset;
        Near(new Vector3d(Geometry.Forearm, wristHeight, 0.0), frames.Wrist);
        Near(new Vector3d(0.0, -1.0, 0.0), frames.ToolDirection, 1e-9);
        Near(new Vector3d(Geometry.Forearm, wristHeight - Geometry.WristToFlange - Geometry.ToolLength, 0.0), frames.Tip);
    }

    [Fact]
    public void At_home_every_link_is_unturned_and_sits_on_its_joint()
    {
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home);
        IReadOnlyList<Vector3d> joints = RobotKinematics.HomeJoints(Geometry);

        Assert.Equal(RobotKinematics.LinkCount, frames.Links.Count);
        for (int i = 0; i < RobotKinematics.LinkCount; i++)
        {
            Near(joints[i], frames.Links[i].Origin, 1e-9);
            Near(new Vector3d(1, 0, 0), frames.Links[i].Rotation * new Vector3d(1, 0, 0), 1e-9);
            Near(new Vector3d(0, 1, 0), frames.Links[i].Rotation * new Vector3d(0, 1, 0), 1e-9);
        }
    }

    [Fact]
    public void Axis_1_at_90_degrees_turns_the_tip_a_quarter_turn_like_a_yaw()
    {
        // A positive yaw takes +X towards -Z, as for every item in a scene.
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home with { A1 = 90.0 });
        RobotFrames home = RobotKinematics.Forward(Geometry, RobotAngles.Home);

        Near(new Vector3d(0.0, home.Tip.Y, -home.Tip.X), frames.Tip);
    }

    [Fact]
    public void Positive_axis_2_leans_the_upper_arm_forward()
    {
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home with { A2 = 90.0 });

        // The elbow goes from straight above axis 2 to straight in front of it.
        Near(new Vector3d(Geometry.UpperArm, Geometry.BaseHeight, 0.0), frames.Links[3].Origin);
    }

    [Fact]
    public void Positive_axis_3_lowers_the_forearm()
    {
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home with { A3 = 90.0 });

        double elbow = Geometry.BaseHeight + Geometry.UpperArm;
        Near(new Vector3d(Geometry.ElbowOffset, elbow - Geometry.Forearm, 0.0), frames.Wrist);
    }

    [Fact]
    public void The_stretched_arm_reaches_the_published_reach()
    {
        // Upper arm forward and the forearm in line with it: the line from the elbow to the wrist
        // is tilted by the elbow offset, so axis 3 is not -90 but -90 plus that angle.
        double offset = Math.Atan2(Geometry.ElbowOffset, Geometry.Forearm) * 180.0 / Math.PI;
        RobotFrames frames = RobotKinematics.Forward(Geometry, RobotAngles.Home with { A2 = 90.0, A3 = -90.0 + offset });

        Near(new Vector3d(Geometry.Reach, Geometry.BaseHeight, 0.0), frames.Wrist);
    }

    [Fact]
    public void Solve_then_forward_returns_the_point_over_a_grid_within_reach()
    {
        int solved = 0;
        RobotAngles seed = RobotAngles.Home;
        for (double x = -0.5; x <= 0.5; x += 0.1)
        {
            for (double z = -0.5; z <= 0.5; z += 0.1)
            {
                for (double y = -0.1; y <= 0.5; y += 0.1)
                {
                    var target = new Vector3d(x, y, z);
                    RobotAngles? angles = RobotKinematics.Solve(Geometry, target, toolDown: true, seed);
                    if (angles is null)
                    {
                        continue;
                    }

                    solved++;
                    RobotFrames frames = RobotKinematics.Forward(Geometry, angles.Value);
                    Near(target, frames.Tip);
                    Near(new Vector3d(0, -1, 0), frames.ToolDirection, 1e-6);
                    Assert.True(Geometry.WithinLimits(angles.Value), $"{target}: {angles.Value} is outside the axis ranges");
                }
            }
        }

        Assert.True(solved > 200, $"only {solved} points solved");
    }

    [Fact]
    public void A_point_out_of_reach_has_no_solution()
    {
        Assert.Null(RobotKinematics.Solve(Geometry, new Vector3d(0.9, 0.3, 0.0), toolDown: true, RobotAngles.Home));
        Assert.Null(RobotKinematics.Solve(Geometry, new Vector3d(0.0, 1.5, 0.0), toolDown: true, RobotAngles.Home));
    }

    [Fact]
    public void Solve_of_the_home_tip_gives_home()
    {
        RobotFrames home = RobotKinematics.Forward(Geometry, RobotAngles.Home);
        RobotAngles? angles = RobotKinematics.Solve(Geometry, home.Tip, toolDown: true, RobotAngles.Home);

        Assert.NotNull(angles);
        Assert.True(angles.Value.MaxDifference(RobotAngles.Home) < 1e-6, angles.Value.ToString());
    }

    [Fact]
    public void Two_points_close_together_give_angles_close_together()
    {
        RobotAngles seed = RobotAngles.Home;
        RobotAngles? previous = null;
        for (double x = 0.25; x <= 0.45; x += 0.002)
        {
            RobotAngles? angles = RobotKinematics.Solve(Geometry, new Vector3d(x, 0.1, 0.2), toolDown: true, seed);
            Assert.NotNull(angles);
            if (previous is not null)
            {
                Assert.True(angles.Value.MaxDifference(previous.Value) < 2.0, $"jump at x = {x}");
            }

            previous = angles;
            seed = angles.Value;
        }
    }

    [Fact]
    public void Solve_keeps_axis_6_of_the_seed()
    {
        RobotAngles? angles = RobotKinematics.Solve(Geometry, new Vector3d(0.3, 0.2, 0.1), toolDown: true,
            RobotAngles.Home with { A6 = 45.0 });

        Assert.NotNull(angles);
        Assert.Equal(45.0, angles.Value.A6, 6);
    }

    [Fact]
    public void Solve_without_tool_down_keeps_the_tool_direction_of_the_seed()
    {
        var seed = new RobotAngles(10.0, 20.0, -30.0, 15.0, 40.0, 0.0);
        RobotFrames start = RobotKinematics.Forward(Geometry, seed);
        Vector3d target = start.Tip + new Vector3d(0.02, -0.03, 0.01);

        RobotAngles? angles = RobotKinematics.Solve(Geometry, target, toolDown: false, seed);

        Assert.NotNull(angles);
        RobotFrames frames = RobotKinematics.Forward(Geometry, angles.Value);
        Near(target, frames.Tip);
        Near(start.ToolDirection, frames.ToolDirection, 1e-6);
    }

    [Theory]
    // The points of the self-test, relative to the foot of a robot on its default pedestal
    // (0.75 m) between two belts at Z = -0.75 and +0.75 m: the items lie 0.5 m from the robot.
    [InlineData(0.0, 0.40, -0.5)]
    [InlineData(0.0, 0.195, -0.5)]
    [InlineData(0.0, 0.40, 0.5)]
    [InlineData(0.0, 0.205, 0.5)]
    public void The_points_of_the_self_test_are_within_reach(double x, double y, double z)
    {
        RobotAngles? angles = RobotKinematics.Solve(Geometry, new Vector3d(x, y, z), toolDown: true, RobotAngles.Home);

        Assert.NotNull(angles);
    }
}
