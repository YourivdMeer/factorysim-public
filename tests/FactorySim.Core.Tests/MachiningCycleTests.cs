using FactorySim.Core;
using Xunit;

namespace FactorySim.Core.Tests;

public class MachiningCycleTests
{
    private const double Dt = 1.0 / 120.0;

    private static MachiningCycle NewCycle() => new(entryAngle: 120.0, swingSpeed: 120.0, liftTime: 0.5, machineTime: 3.0);

    /// <summary>Ticks until the step changes; returns the seconds that took.</summary>
    private static double RunUntilStepChanges(MachiningCycle cycle, bool start = false, bool entry = false, bool exitClear = true)
    {
        MachiningStep step = cycle.Step;
        double seconds = 0.0;
        while (cycle.Step == step && seconds < 60.0)
        {
            cycle.Tick(Dt, start, entry, exitClear);
            seconds += Dt;
        }

        return seconds;
    }

    [Fact]
    public void It_rests_parked_until_start_is_given_with_a_part_at_the_entry()
    {
        MachiningCycle cycle = NewCycle();

        cycle.Tick(Dt, start: true, partAtEntry: false, exitClear: true);
        Assert.Equal(MachiningStep.Idle, cycle.Step);
        cycle.Tick(Dt, start: false, partAtEntry: true, exitClear: true);
        Assert.False(cycle.Busy);
        Assert.Equal(MachiningCycle.ParkAngle, cycle.ArmAngle);
        Assert.Equal(0.0, cycle.Lowered);
        Assert.False(cycle.Holding);

        cycle.Tick(Dt, start: true, partAtEntry: true, exitClear: true);
        Assert.True(cycle.Busy);
        Assert.Equal(MachiningStep.ToEntry, cycle.Step);
    }

    [Fact]
    public void A_cycle_runs_through_all_steps_in_order_and_ends_parked()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);

        var steps = new List<MachiningStep> { cycle.Step };
        while (cycle.Busy && steps.Count < 30)
        {
            RunUntilStepChanges(cycle);
            steps.Add(cycle.Step);
        }

        Assert.Equal(
            new[]
            {
                MachiningStep.ToEntry, MachiningStep.PickDown, MachiningStep.PickUp, MachiningStep.ToMachine,
                MachiningStep.LoadDown, MachiningStep.LoadUp, MachiningStep.Clear, MachiningStep.Machining,
                MachiningStep.Return, MachiningStep.UnloadDown, MachiningStep.UnloadUp, MachiningStep.ToExit,
                MachiningStep.DropDown, MachiningStep.DropUp, MachiningStep.Park, MachiningStep.Idle,
            },
            steps);
        Assert.Equal(MachiningCycle.ParkAngle, cycle.ArmAngle);
        Assert.False(cycle.Machined);
    }

    [Fact]
    public void Swings_take_their_angle_over_the_speed_and_machining_takes_the_machine_time()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);

        Assert.Equal(0.5, RunUntilStepChanges(cycle), 1);       // park (-180) to entry (-120) at 120 deg/s
        Assert.Equal(-120.0, cycle.ArmAngle, 1);
        Assert.Equal(0.5, RunUntilStepChanges(cycle), 1);       // down
        Assert.Equal(1.0, cycle.Lowered, 1);
        RunUntilStepChanges(cycle);                             // up
        Assert.Equal(1.0, RunUntilStepChanges(cycle), 1);       // entry to machine
        Assert.Equal(0.0, cycle.ArmAngle, 1);
        while (cycle.Step != MachiningStep.Machining)
        {
            RunUntilStepChanges(cycle);
        }

        Assert.Equal(-MachiningCycle.ClearAngle, cycle.ArmAngle, 1);
        Assert.Equal(3.0, RunUntilStepChanges(cycle), 1);
    }

    [Fact]
    public void The_gripper_holds_the_part_from_entry_to_machine_and_from_machine_to_exit()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        var holding = new Dictionary<MachiningStep, bool>();
        while (cycle.Busy)
        {
            holding[cycle.Step] = cycle.Holding;
            cycle.Tick(Dt, false, false, true);
        }

        MachiningStep[] held =
        {
            MachiningStep.PickUp, MachiningStep.ToMachine, MachiningStep.LoadDown,
            MachiningStep.UnloadUp, MachiningStep.ToExit, MachiningStep.DropDown,
        };
        Assert.All(holding, pair => Assert.Equal(held.Contains(pair.Key), pair.Value));
    }

    [Fact]
    public void The_spindle_comes_down_during_machining_and_the_part_is_machined_when_it_lifts()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        while (cycle.Step != MachiningStep.Machining)
        {
            Assert.Equal(0.0, cycle.Spindle);
            Assert.False(cycle.Machined);
            cycle.Tick(Dt, false, false, true);
        }

        double deepest = 0.0;
        bool machinedWhileDown = false;
        while (cycle.Step == MachiningStep.Machining)
        {
            deepest = Math.Max(deepest, cycle.Spindle);
            machinedWhileDown |= cycle.Machined && cycle.Spindle > 0.99;
            cycle.Tick(Dt, false, false, true);
        }

        Assert.Equal(1.0, deepest, 2);
        Assert.False(machinedWhileDown);
        Assert.True(cycle.Machined);
        Assert.Equal(0.0, cycle.Spindle, 1);
    }

    [Fact]
    public void It_waits_above_the_exit_until_the_exit_is_clear()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        while (cycle.Step != MachiningStep.ToExit)
        {
            cycle.Tick(Dt, false, false, true);
        }

        for (int i = 0; i < 600; i++)
        {
            cycle.Tick(Dt, false, false, exitClear: false);
        }

        Assert.Equal(MachiningStep.ToExit, cycle.Step);
        Assert.True(cycle.WaitingForExit);
        Assert.True(cycle.Holding);
        Assert.Equal(120.0, cycle.ArmAngle, 3);

        cycle.Tick(Dt, false, false, exitClear: true);
        Assert.Equal(MachiningStep.DropDown, cycle.Step);
        Assert.False(cycle.WaitingForExit);
    }

    [Fact]
    public void The_arm_never_jumps_while_busy()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        double angle = cycle.ArmAngle;
        double lowered = cycle.Lowered;
        while (cycle.Busy)
        {
            cycle.Tick(Dt, false, false, true);
            if (cycle.Busy)
            {
                Assert.InRange(Math.Abs(cycle.ArmAngle - angle), 0.0, 120.0 * Dt + 1e-6);
                Assert.InRange(Math.Abs(cycle.Lowered - lowered), 0.0, Dt / 0.5 + 1e-6);
            }

            angle = cycle.ArmAngle;
            lowered = cycle.Lowered;
        }
    }

    [Fact]
    public void A_start_that_stays_on_begins_the_next_cycle_when_a_part_is_waiting()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        while (cycle.Busy)
        {
            cycle.Tick(Dt, true, true, true);
        }

        cycle.Tick(Dt, true, true, true);
        Assert.Equal(MachiningStep.ToEntry, cycle.Step);
    }

    [Fact]
    public void Abort_brings_the_arm_back_to_park_without_holding_anything()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        while (cycle.Step != MachiningStep.PickUp)
        {
            cycle.Tick(Dt, false, false, true);
        }

        cycle.Abort();
        Assert.Equal(MachiningStep.Abort, cycle.Step);
        Assert.False(cycle.Holding);
        Assert.Equal(-120.0, cycle.ArmAngle, 1);
        RunUntilStepChanges(cycle);
        Assert.Equal(MachiningStep.Idle, cycle.Step);
        Assert.Equal(MachiningCycle.ParkAngle, cycle.ArmAngle);
        Assert.Equal(0.0, cycle.Lowered);
    }

    [Fact]
    public void Reset_returns_to_rest()
    {
        MachiningCycle cycle = NewCycle();
        cycle.Tick(Dt, true, true, true);
        for (int i = 0; i < 400; i++)
        {
            cycle.Tick(Dt, false, false, true);
        }

        cycle.Reset();
        Assert.False(cycle.Busy);
        Assert.False(cycle.Holding);
        Assert.Equal(MachiningCycle.ParkAngle, cycle.ArmAngle);
    }

    [Theory]
    [InlineData("raw_material_blue", false, "product_base_blue")]
    [InlineData("raw_material_blue", true, "product_lid_blue")]
    [InlineData("raw_material_metal", true, "product_lid_metal")]
    [InlineData("pallet", false, null)]
    [InlineData("product_base_blue", true, null)]
    [InlineData(null, false, null)]
    public void Raw_material_becomes_a_base_or_a_lid_of_the_same_colour(string? item, bool lid, string? expected)
    {
        Assert.Equal(expected, MachiningCycle.Product(item, lid));
    }

    [Fact]
    public void Bad_settings_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachiningCycle(0.0, 120.0, 0.5, 3.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachiningCycle(120.0, 0.0, 0.5, 3.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachiningCycle(120.0, 120.0, 0.0, 3.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachiningCycle(120.0, 120.0, 0.5, 0.0));
    }
}
