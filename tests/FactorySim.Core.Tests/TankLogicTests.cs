namespace FactorySim.Core.Tests;

public class TankLogicTests
{
    private static TankLogic CreateTank() => new(height: 3.0, diameter: 2.0, maxInflow: 0.2, maxOutflow: 0.2);

    private static void Run(TankLogic tank, double seconds, double fill, double discharge)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 120); i++)
        {
            tank.Step(1.0 / 120, fill, discharge);
        }
    }

    [Fact]
    public void An_empty_tank_with_closed_valves_stays_empty()
    {
        TankLogic tank = CreateTank();

        Run(tank, 10, 0, 1);

        Assert.Equal(0.0, tank.Level);
        Assert.Equal(0.0, tank.Outflow);
    }

    [Fact]
    public void Filling_raises_the_level_by_inflow_over_the_area()
    {
        TankLogic tank = CreateTank();

        Run(tank, 10, 1, 0);

        double area = Math.PI * 2.0 * 2.0 / 4.0;
        Assert.Equal(0.2 * 10 / area, tank.Level, 3);
        Assert.Equal(0.2, tank.Inflow, 6);
    }

    [Fact]
    public void A_half_open_fill_valve_gives_half_the_inflow()
    {
        TankLogic tank = CreateTank();

        Run(tank, 10, 0.5, 0);

        double area = Math.PI;
        Assert.Equal(0.1 * 10 / area, tank.Level, 3);
    }

    [Fact]
    public void Discharge_depends_on_the_root_of_the_level()
    {
        TankLogic full = CreateTank();
        full.SetLevel(3.0);
        TankLogic quarter = CreateTank();
        quarter.SetLevel(0.75);

        full.Step(1.0 / 120, 0, 1);
        quarter.Step(1.0 / 120, 0, 1);

        Assert.Equal(0.2, full.Outflow, 2);
        Assert.Equal(0.1, quarter.Outflow, 2);
    }

    [Fact]
    public void Emptying_lowers_the_level_to_zero_and_no_further()
    {
        TankLogic tank = CreateTank();
        tank.SetLevel(1.0);

        Run(tank, 30, 0, 1);
        double after30 = tank.Level;
        Run(tank, 600, 0, 1);

        Assert.InRange(after30, 0.01, 0.99);
        Assert.Equal(0.0, tank.Level, 3);
        Assert.True(tank.Level >= 0.0);
    }

    [Fact]
    public void The_level_settles_where_inflow_and_outflow_match()
    {
        TankLogic tank = CreateTank();

        Run(tank, 600, 0.5, 1);

        // 0.1 = 0.2 * sqrt(h / 3)  =>  h = 3 / 4
        Assert.Equal(0.75, tank.Level, 2);
        Assert.Equal(tank.Inflow, tank.Outflow, 3);
    }

    [Fact]
    public void A_full_tank_overflows_and_stays_full()
    {
        TankLogic tank = CreateTank();

        Run(tank, 120, 1, 0);

        Assert.Equal(3.0, tank.Level, 6);
        Assert.True(tank.Overflowing);
        Run(tank, 1, 0, 0);
        Assert.False(tank.Overflowing);
    }

    [Fact]
    public void Valve_positions_outside_zero_to_one_are_clipped()
    {
        TankLogic tank = CreateTank();

        Run(tank, 1, 5, -3);

        Assert.Equal(0.2, tank.Inflow, 6);
    }

    [Fact]
    public void Reset_empties_the_tank()
    {
        TankLogic tank = CreateTank();
        Run(tank, 5, 1, 0);

        tank.Reset();

        Assert.Equal(0.0, tank.Level);
        Assert.Equal(0.0, tank.Inflow);
        Assert.False(tank.Overflowing);
    }
}
