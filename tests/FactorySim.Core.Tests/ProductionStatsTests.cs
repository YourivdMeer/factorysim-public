namespace FactorySim.Core.Tests;

public class ProductionStatsTests
{
    private const double Tick = 1.0 / 120.0;

    /// <summary>Runs the clock from <paramref name="from"/> to <paramref name="to"/> in ticks of 1/120 s.</summary>
    private static double Run(ProductionStats stats, double from, double to, Func<double, bool>? conveyorOn = null)
    {
        double time = from;
        while (time < to - 1e-9)
        {
            time += Tick;
            stats.Tick(time, Tick, new[] { ("Conveyor", conveyorOn?.Invoke(time) ?? true) });
        }

        return time;
    }

    [Fact]
    public void A_fixed_interval_gives_sixty_divided_by_the_interval_per_minute()
    {
        var stats = new ProductionStats();
        stats.AddRemover("End");
        double time = 0.0;
        for (int i = 1; i <= 40; i++)
        {
            time = Run(stats, time, i * 3.0);
            stats.Removed("End", time - 5.0, time);
        }

        RemoverStats end = stats.Remover("End")!;
        Assert.Equal(40, end.Count);
        Assert.Equal(20.0, end.PerMinute, 6);
        Assert.Equal(20.0, end.PerMinuteWindow, 6);
        Assert.Equal(20.0, stats.PerMinute, 6);
        Assert.Equal(120.0, stats.RunTime, 6);
    }

    [Fact]
    public void The_window_counts_only_the_last_sixty_seconds()
    {
        var stats = new ProductionStats();
        double time = 0.0;
        for (int i = 1; i <= 30; i++)
        {
            time = Run(stats, time, i * 2.0);       // 30 per minute for the first minute
            stats.Removed("End", double.NaN, time);
        }

        time = Run(stats, time, 120.0);             // then nothing for a minute

        RemoverStats end = stats.Remover("End")!;
        Assert.Equal(0.0, end.PerMinuteWindow, 6);
        Assert.Equal(15.0, end.PerMinute, 6);

        for (int i = 1; i <= 6; i++)
        {
            time = Run(stats, time, 120.0 + i * 5.0);
            stats.Removed("End", double.NaN, time);
        }

        // 6 items in the last 60 s.
        Assert.Equal(6.0, stats.Remover("End")!.PerMinuteWindow, 6);
        Assert.Equal(6.0, stats.PerMinuteWindow, 6);
    }

    [Fact]
    public void Before_a_minute_has_passed_the_window_divides_by_the_run_time()
    {
        var stats = new ProductionStats();
        double time = 0.0;
        for (int i = 1; i <= 5; i++)
        {
            time = Run(stats, time, i * 2.0);
            stats.Removed("End", double.NaN, time);
        }

        // 5 items in 10 s is 30 per minute, not 5.
        Assert.Equal(30.0, stats.Remover("End")!.PerMinuteWindow, 6);
    }

    [Fact]
    public void On_time_counts_only_the_ticks_the_actuator_was_on()
    {
        var stats = new ProductionStats();

        Run(stats, 0.0, 60.0, time => (int)Math.Floor((time - 1e-9) / 5.0) % 2 == 0);

        ActuatorStats conveyor = Assert.Single(stats.Actuators);
        Assert.Equal("Conveyor", conveyor.Tag);
        Assert.Equal(30.0, conveyor.OnSeconds, 3);
        Assert.Equal(0.5, conveyor.OnFraction, 3);
    }

    [Fact]
    public void A_tick_without_time_adds_nothing()
    {
        var stats = new ProductionStats();
        Run(stats, 0.0, 1.0);

        // Paused: the scene keeps calling Tick with dt = 0.
        stats.Tick(1.0, 0.0, new[] { ("Conveyor", true) });

        Assert.Equal(1.0, stats.RunTime, 6);
        Assert.Equal(1.0, stats.Actuators[0].OnSeconds, 6);
    }

    [Fact]
    public void Lead_time_is_from_spawn_to_removal()
    {
        var stats = new ProductionStats();
        stats.Removed("End", 1.0, 7.0);
        stats.Removed("End", 2.0, 10.0);
        stats.Removed("End", 3.0, 10.0);

        RemoverStats end = stats.Remover("End")!;
        Assert.Equal(3, end.LeadTimeCount);
        Assert.Equal(7.0, end.LeadTimeAverage!.Value, 6);
        Assert.Equal(6.0, end.LeadTimeMin!.Value, 6);
        Assert.Equal(8.0, end.LeadTimeMax!.Value, 6);
    }

    [Fact]
    public void An_item_without_a_spawn_time_counts_but_has_no_lead_time()
    {
        var stats = new ProductionStats();
        stats.Removed("End", 2.0, 6.0);
        stats.Removed("End", double.NaN, 8.0);
        stats.Removed("End", -1.0, 9.0);

        RemoverStats end = stats.Remover("End")!;
        Assert.Equal(3, end.Count);
        Assert.Equal(1, end.LeadTimeCount);
        Assert.Equal(4.0, end.LeadTimeAverage!.Value, 6);
    }

    [Fact]
    public void A_remover_without_items_gives_zeros_and_no_lead_time()
    {
        var stats = new ProductionStats();
        stats.AddRemover("Lane");

        RemoverStats lane = stats.Remover("Lane")!;
        Assert.Equal(0, lane.Count);
        Assert.Equal(0.0, lane.PerMinute);
        Assert.Equal(0.0, lane.PerMinuteWindow);
        Assert.Null(lane.LeadTimeAverage);
        Assert.Null(lane.LeadTimeMin);
        Assert.Null(lane.LeadTimeMax);
        Assert.Equal(0.0, stats.PerMinute);
        Assert.Empty(stats.Actuators);
    }

    [Fact]
    public void Removers_keep_the_order_they_were_added_in()
    {
        var stats = new ProductionStats();
        stats.AddRemover("End");
        stats.AddRemover("Lane");
        stats.Removed("Lane", double.NaN, 1.0);
        stats.Removed("Other", double.NaN, 1.0);
        stats.AddRemover("End");

        Assert.Equal(new[] { "End", "Lane", "Other" }, stats.Removers.Select(r => r.Name));
    }

    [Fact]
    public void Totals_count_emitted_removed_and_fallen()
    {
        var stats = new ProductionStats();
        stats.Emitted("Emitter", 1.0);
        stats.Emitted("Emitter", 2.0);
        stats.Emitted("Pallets", 2.0);
        stats.Removed("End", double.NaN, 5.0);
        stats.Removed("Lane", double.NaN, 6.0);
        stats.Fallen(7.0);

        Assert.Equal(3, stats.EmittedCount);
        Assert.Equal(2, stats.RemovedCount);
        Assert.Equal(1, stats.FallenCount);
    }

    [Fact]
    public void Reset_puts_everything_back_to_zero()
    {
        var stats = new ProductionStats();
        stats.AddRemover("End");
        double time = Run(stats, 0.0, 10.0);
        stats.Emitted("Emitter", time);
        stats.Removed("End", 1.0, time);
        stats.Fallen(time);

        stats.Reset();

        Assert.Equal(0.0, stats.RunTime);
        Assert.Equal(0, stats.EmittedCount);
        Assert.Equal(0, stats.RemovedCount);
        Assert.Equal(0, stats.FallenCount);
        Assert.Equal(0.0, stats.PerMinute);
        Assert.Empty(stats.Removers);
        Assert.Empty(stats.Actuators);

        // The window starts again too.
        time = Run(stats, 0.0, 6.0);
        stats.Removed("End", double.NaN, time);
        Assert.Equal(10.0, stats.Remover("End")!.PerMinuteWindow, 6);
    }

    [Fact]
    public void The_csv_has_a_row_for_the_total_every_remover_and_every_actuator()
    {
        var stats = new ProductionStats();
        stats.AddRemover("End");
        stats.AddRemover("Lane");
        double time = Run(stats, 0.0, 60.0, t => t <= 15.0);
        stats.Emitted("Emitter", 1.0);
        stats.Removed("End", 1.0, 7.5);

        string[] lines = stats.ToCsv().TrimEnd('\n').Split('\n');

        Assert.Equal("Kind;Name;Count;PerMinute;PerMinuteLast60s;LeadTimeAvg;LeadTimeMin;LeadTimeMax;OnSeconds;OnPercent", lines[0]);
        Assert.Equal("total;Run time 60.000 s, emitted 1, fallen 0;1;1.000;1.000;6.500;6.500;6.500;;", lines[1]);
        Assert.Equal("remover;End;1;1.000;1.000;6.500;6.500;6.500;;", lines[2]);
        Assert.Equal("remover;Lane;0;0.000;0.000;;;;;", lines[3]);
        Assert.Equal("actuator;Conveyor;;;;;;;15.000;25.0", lines[4]);
        Assert.Equal(5, lines.Length);
        Assert.True(time > 59.9);
    }
}
