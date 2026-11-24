namespace FactorySim.Core.Tests;

public class StackerCraneTests
{
    private const double Dt = 0.01;

    private static StackerCrane NewCrane(bool jog = false) => new(
        new StackerPositions(1.5, 1.2, 0.3, 0.5, 0.03), jog,
        driveSpeed: 0.5, hoistSpeed: 0.4, forkStroke: 1.4, forkSpeed: 0.7, liftStroke: 0.10, liftSpeed: 0.1);

    private static void Run(StackerCrane crane, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / Dt); i++)
        {
            crane.Tick(Dt);
        }
    }

    private static void SetTarget(StackerCrane crane, int number)
    {
        crane.Target1 = (number & 1) != 0;
        crane.Target2 = (number & 2) != 0;
        crane.Target4 = (number & 4) != 0;
        crane.Target8 = (number & 8) != 0;
    }

    private static void Go(StackerCrane crane, int number)
    {
        SetTarget(crane, number);
        crane.Go = true;
        crane.Tick(Dt);
        crane.Go = false;
        crane.Tick(Dt);                             // a cycle with go off, so the next go is a new flank
    }

    private static void RunUntilIdle(StackerCrane crane)
    {
        for (int i = 0; i < 6000 && crane.Busy; i++)
        {
            crane.Tick(Dt);
        }

        Assert.False(crane.Busy);
    }

    [Fact]
    public void Rests_at_the_in_position_with_the_forks_in_the_middle()
    {
        StackerCrane crane = NewCrane();

        Run(crane, 1.0);

        Assert.True(crane.ForksMiddle);
        Assert.False(crane.Busy);
        Assert.False(crane.Error);
        Assert.False(crane.Lifted);
        Assert.True(crane.AtTarget);                 // the target bits say 0 and that is where it stands
        Assert.Equal(-1.5, crane.X, 6);
        Assert.Equal(0.47, crane.PlatformHeight, 6);
    }

    [Fact]
    public void Go_sends_the_crane_to_the_slot_the_bits_name()
    {
        StackerCrane crane = NewCrane();

        Go(crane, 5);
        Assert.True(crane.Busy);
        Assert.False(crane.AtTarget);

        RunUntilIdle(crane);

        Assert.True(crane.AtTarget);
        Assert.Equal(0.0, crane.X, 2);
        Assert.Equal(1.47, crane.PlatformHeight, 2);
        Assert.False(crane.Error);
    }

    [Fact]
    public void Drives_and_hoists_at_the_same_time_so_the_slower_axis_decides()
    {
        StackerCrane crane = NewCrane();

        Go(crane, 12);                              // x from -1.5 to 1.5 (6 s), up from 0.47 to 3.87 (8.5 s)
        Run(crane, 7.0);
        Assert.True(crane.Busy);
        Assert.Equal(1.5, crane.X, 2);

        Run(crane, 2.0);
        Assert.False(crane.Busy);
        Assert.Equal(3.87, crane.PlatformHeight, 2);
    }

    [Fact]
    public void A_target_change_while_go_stays_on_does_nothing()
    {
        StackerCrane crane = NewCrane();
        SetTarget(crane, 3);
        crane.Go = true;
        crane.Tick(Dt);
        RunUntilIdle(crane);

        SetTarget(crane, 12);
        Run(crane, 1.0);

        Assert.False(crane.Busy);
        Assert.Equal(1.5, crane.X, 2);
        Assert.Equal(0.27, crane.PlatformHeight, 2);
        Assert.False(crane.AtTarget);               // it stands on slot 3, not on 12
    }

    [Fact]
    public void A_new_go_flank_starts_the_next_trip()
    {
        StackerCrane crane = NewCrane();
        Go(crane, 3);
        RunUntilIdle(crane);

        Go(crane, 1);
        RunUntilIdle(crane);

        Assert.Equal(-1.5, crane.X, 2);
        Assert.True(crane.AtTarget);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    public void A_target_that_does_not_exist_raises_error_and_the_crane_stays(int number)
    {
        StackerCrane crane = NewCrane();

        Go(crane, number);
        Run(crane, 1.0);

        Assert.True(crane.Error);
        Assert.False(crane.Busy);
        Assert.False(crane.AtTarget);
        Assert.Equal(-1.5, crane.X, 6);
        Assert.Equal(0.47, crane.PlatformHeight, 6);
    }

    [Fact]
    public void A_valid_go_clears_the_error()
    {
        StackerCrane crane = NewCrane();
        Go(crane, 15);
        Assert.True(crane.Error);

        Go(crane, 2);

        Assert.False(crane.Error);
        Assert.True(crane.Busy);
    }

    [Fact]
    public void Go_with_the_forks_out_is_ignored_and_reported()
    {
        StackerCrane crane = NewCrane();
        crane.ForksRight = true;
        Run(crane, 3.0);
        Assert.True(crane.ForksRightEnd);

        Go(crane, 5);
        Run(crane, 1.0);

        Assert.True(crane.Error);
        Assert.False(crane.Busy);
        Assert.Equal(-1.5, crane.X, 6);

        // Once the forks are back the same go works.
        crane.ForksRight = false;
        Run(crane, 3.0);
        Go(crane, 5);
        Assert.True(crane.Busy);
        Assert.False(crane.Error);
    }

    [Fact]
    public void Driving_pauses_while_the_forks_are_out()
    {
        StackerCrane crane = NewCrane();
        Go(crane, 3);
        Run(crane, 1.0);
        double x = crane.X;
        Assert.True(x > -1.5);

        crane.ForksLeft = true;
        Run(crane, 3.0);
        double stopped = crane.X;
        Assert.True(stopped - x < 0.02);            // at most the steps before the forks left the middle
        Assert.True(crane.Busy);                    // still wants to go

        crane.ForksLeft = false;
        RunUntilIdle(crane);
        Assert.Equal(1.5, crane.X, 2);
    }

    [Fact]
    public void The_lift_is_separate_from_the_height_of_the_target()
    {
        StackerCrane crane = NewCrane();
        Go(crane, 5);
        RunUntilIdle(crane);

        crane.Lift = true;
        Run(crane, 1.5);

        Assert.True(crane.Lifted);
        Assert.Equal(0.10, crane.LiftOffset, 6);
        Assert.Equal(1.47, crane.PlatformHeight, 2);
        Assert.True(crane.AtTarget);                // the target height does not include the lift

        crane.Lift = false;
        Run(crane, 1.5);
        Assert.False(crane.Lifted);
        Assert.Equal(0.0, crane.LiftOffset, 6);
    }

    [Fact]
    public void The_forks_report_their_ends()
    {
        StackerCrane crane = NewCrane();

        crane.ForksLeft = true;
        Run(crane, 0.5);
        Assert.False(crane.ForksMiddle);
        Assert.False(crane.ForksLeftEnd);

        Run(crane, 2.0);
        Assert.True(crane.ForksLeftEnd);
        Assert.Equal(-1.4, crane.ForkOffset, 6);

        crane.ForksLeft = false;
        Run(crane, 3.0);
        Assert.True(crane.ForksMiddle);
    }

    [Fact]
    public void Jog_mode_drives_while_commanded_and_reports_the_column()
    {
        StackerCrane crane = NewCrane(jog: true);
        Assert.True(crane.AtColumn(1));
        Assert.False(crane.AtColumn(2));

        crane.Right = true;
        Run(crane, 2.5);
        Assert.False(crane.AtColumn(2));
        Run(crane, 0.5);                            // x = -1.5 + 3.0 s * 0.5 m/s = 0.0
        crane.Right = false;
        Run(crane, 1.0);

        Assert.True(crane.AtColumn(2));
        Assert.False(crane.AtColumn(1));
        Assert.Equal(0.0, crane.X, 2);
    }

    [Fact]
    public void Jog_mode_hoists_and_reports_the_level()
    {
        StackerCrane crane = NewCrane(jog: true);
        Assert.True(crane.AtLevel(0));

        crane.Up = true;
        Run(crane, 5.0);                            // 0.47 + 2.0 = 2.47: between levels
        for (int level = 0; level <= 4; level++)
        {
            Assert.False(crane.AtLevel(level));
        }

        Run(crane, 0.5);                            // 2.67 = level 3
        crane.Up = false;

        Assert.True(crane.AtLevel(3));
    }

    [Fact]
    public void Jog_mode_stops_at_the_end_of_the_travel()
    {
        StackerCrane crane = NewCrane(jog: true);
        crane.Left = true;
        Run(crane, 30.0);
        double left = crane.X;
        crane.Left = false;
        crane.Right = true;
        Run(crane, 60.0);

        Assert.True(left < -1.5);
        Assert.True(crane.X > 1.5);
        Assert.True(crane.X < 3.0);
    }

    [Fact]
    public void Jog_mode_does_not_drive_or_hoist_while_the_forks_are_out()
    {
        StackerCrane crane = NewCrane(jog: true);
        crane.ForksLeft = true;
        Run(crane, 3.0);
        double x = crane.X;
        double height = crane.PlatformHeight;

        crane.Right = true;
        crane.Up = true;
        Run(crane, 2.0);

        Assert.Equal(x, crane.X, 6);
        Assert.Equal(height, crane.PlatformHeight, 6);
    }

    [Fact]
    public void Jog_mode_ignores_go_and_both_directions_do_nothing()
    {
        StackerCrane crane = NewCrane(jog: true);
        Go(crane, 5);
        Run(crane, 1.0);
        Assert.Equal(-1.5, crane.X, 6);
        Assert.False(crane.Busy);

        crane.Left = true;
        crane.Right = true;
        Run(crane, 1.0);
        Assert.Equal(-1.5, crane.X, 6);
    }

    [Fact]
    public void Reset_returns_to_rest()
    {
        StackerCrane crane = NewCrane();
        Go(crane, 12);
        Run(crane, 3.0);
        crane.ForksLeft = true;
        crane.Lift = true;
        Run(crane, 1.0);

        crane.Reset();

        Assert.Equal(-1.5, crane.X, 6);
        Assert.Equal(0.47, crane.PlatformHeight, 6);
        Assert.True(crane.ForksMiddle);
        Assert.False(crane.Lifted);
        Assert.False(crane.Busy);
        Assert.False(crane.Error);
        Assert.False(crane.ForksLeft);
        Assert.False(crane.Lift);
    }
}
