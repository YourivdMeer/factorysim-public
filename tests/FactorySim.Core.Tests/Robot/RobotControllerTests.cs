using FactorySim.Core.Robot;

namespace FactorySim.Core.Tests.Robot;

public class RobotControllerTests
{
    private const double Dt = 0.01;

    private static readonly RobotAngles PoseOne = new(90.0, 20.0, 10.0, 0.0, -30.0, 0.0);

    private static RobotController NewRobot()
    {
        var robot = new RobotController(RobotGeometry.Default, speedPercent: 100.0);
        robot.SetPoses(new Dictionary<int, RobotAngles> { [1] = PoseOne, [3] = new RobotAngles(-45, 0, 0, 0, 0, 0) });
        return robot;
    }

    private static void SetPosition(RobotController robot, int number)
    {
        robot.Pos1 = (number & 1) != 0;
        robot.Pos2 = (number & 2) != 0;
        robot.Pos4 = (number & 4) != 0;
        robot.Pos8 = (number & 8) != 0;
    }

    private static void Go(RobotController robot, int number)
    {
        SetPosition(robot, number);
        robot.Go = true;
        robot.Tick(Dt);
        robot.Go = false;
        robot.Tick(Dt);
    }

    private static void RunUntilIdle(RobotController robot)
    {
        for (int i = 0; i < 10000 && robot.Busy; i++)
        {
            robot.Tick(Dt);
        }
    }

    [Fact]
    public void A_go_flank_moves_the_robot_to_the_position_and_reports_it()
    {
        RobotController robot = NewRobot();

        Go(robot, 1);
        Assert.True(robot.Busy);
        Assert.False(robot.AtPosition);
        RunUntilIdle(robot);

        Assert.False(robot.Busy);
        Assert.True(robot.AtPosition);
        Assert.False(robot.Error);
        Assert.Equal(PoseOne, robot.Angles);
    }

    [Fact]
    public void At_position_follows_the_bits()
    {
        RobotController robot = NewRobot();
        Go(robot, 1);
        RunUntilIdle(robot);

        SetPosition(robot, 3);
        robot.Tick(Dt);

        Assert.False(robot.AtPosition);
    }

    [Fact]
    public void Position_0_is_home_and_the_robot_starts_there()
    {
        RobotController robot = NewRobot();

        Assert.True(robot.AtPosition);
        Go(robot, 1);
        RunUntilIdle(robot);
        Go(robot, 0);
        RunUntilIdle(robot);

        Assert.True(robot.AtPosition);
        Assert.Equal(RobotAngles.Home, robot.Angles);
    }

    [Fact]
    public void A_position_that_was_not_taught_gives_an_error_and_no_motion()
    {
        RobotController robot = NewRobot();

        Go(robot, 5);

        Assert.True(robot.Error);
        Assert.False(robot.Busy);
        Assert.False(robot.AtPosition);
        Assert.Equal(RobotAngles.Home, robot.Angles);

        Go(robot, 1);
        Assert.False(robot.Error);
    }

    [Fact]
    public void A_go_during_a_motion_is_ignored()
    {
        RobotController robot = NewRobot();
        Go(robot, 1);

        Go(robot, 3);
        RunUntilIdle(robot);

        Assert.Equal(PoseOne, robot.Angles);
    }

    [Fact]
    public void Holding_go_does_not_start_again()
    {
        RobotController robot = NewRobot();
        SetPosition(robot, 1);
        robot.Go = true;
        robot.Tick(Dt);
        RunUntilIdle(robot);
        SetPosition(robot, 3);
        robot.Tick(Dt);

        Assert.False(robot.Busy);
        Assert.Equal(PoseOne, robot.Angles);
    }

    [Fact]
    public void Reset_goes_home_and_clears_the_commands()
    {
        RobotController robot = NewRobot();
        Go(robot, 5);
        Go(robot, 1);
        robot.Tick(Dt);
        robot.Grip = true;

        robot.Reset();

        Assert.Equal(RobotAngles.Home, robot.Angles);
        Assert.False(robot.Busy);
        Assert.False(robot.Error);
        Assert.False(robot.Grip);
        Assert.False(robot.Go);
    }

    [Fact]
    public void Poses_outside_1_to_15_are_ignored()
    {
        var robot = new RobotController(RobotGeometry.Default, 100.0);
        robot.SetPoses(new Dictionary<int, RobotAngles> { [0] = PoseOne, [16] = PoseOne });

        Assert.True(robot.HasPose(0));
        Assert.Equal(RobotAngles.Home, robot.Pose(0));
        Assert.False(robot.HasPose(16));
    }
}
