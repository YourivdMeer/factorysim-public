using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class EmitterLogicTests
{
    private const double Dt = 1.0 / 120.0;

    private static List<BoxKind> Run(EmitterLogic emitter, double seconds, Func<bool>? occupied = null)
    {
        var emitted = new List<BoxKind>();
        int steps = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            BoxKind? kind = emitter.Tick(Dt, occupied?.Invoke() ?? false);
            if (kind is not null)
            {
                emitted.Add(kind.Value);
            }
        }

        return emitted;
    }

    [Fact]
    public void Disabled_emitter_emits_nothing()
    {
        var emitter = new EmitterLogic(intervalSeconds: 1.0, seed: 1);

        Assert.Empty(Run(emitter, 10.0));
    }

    [Fact]
    public void Enabled_emitter_emits_immediately_and_then_once_per_interval()
    {
        var emitter = new EmitterLogic(intervalSeconds: 2.0, seed: 1) { Enabled = true };

        // t = 0, 2, 4, 6, 8 -> five boxes in just under 10 seconds.
        Assert.Equal(5, Run(emitter, 9.9).Count);
    }

    [Fact]
    public void Occupied_volume_blocks_emission_until_it_is_free()
    {
        var emitter = new EmitterLogic(intervalSeconds: 1.0, seed: 1) { Enabled = true };

        Assert.Empty(Run(emitter, 5.0, occupied: () => true));
        Assert.Single(Run(emitter, 0.5));
    }

    [Fact]
    public void Blocked_time_does_not_cause_a_burst_afterwards()
    {
        var emitter = new EmitterLogic(intervalSeconds: 1.0, seed: 1) { Enabled = true };
        Run(emitter, 10.0, occupied: () => true);

        // One box right away, the next only after a full interval.
        Assert.Equal(2, Run(emitter, 1.5).Count);
    }

    [Fact]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new EmitterLogic(1.0, seed: 42) { Enabled = true };
        var b = new EmitterLogic(1.0, seed: 42) { Enabled = true };

        Assert.Equal(Run(a, 30.0), Run(b, 30.0));
    }

    [Fact]
    public void Sequence_contains_both_low_and_high_boxes()
    {
        var emitter = new EmitterLogic(1.0, seed: 7) { Enabled = true };

        List<BoxKind> kinds = Run(emitter, 40.0);

        Assert.Contains(BoxKind.Low, kinds);
        Assert.Contains(BoxKind.High, kinds);
    }

    [Theory]
    [InlineData(0.0, BoxKind.Low)]
    [InlineData(1.0, BoxKind.High)]
    public void High_ratio_extremes_force_one_kind(double ratio, BoxKind expected)
    {
        var emitter = new EmitterLogic(1.0, seed: 3, highRatio: ratio) { Enabled = true };

        Assert.All(Run(emitter, 20.0), kind => Assert.Equal(expected, kind));
    }

    [Fact]
    public void Reset_restarts_the_sequence_and_counter()
    {
        var emitter = new EmitterLogic(1.0, seed: 11) { Enabled = true };
        List<BoxKind> first = Run(emitter, 10.0);

        emitter.Reset();

        Assert.Equal(0, emitter.EmittedCount);
        Assert.Equal(first, Run(emitter, 10.0));
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmitterLogic(0.0, seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmitterLogic(1.0, seed: 1, highRatio: 1.5));
    }
}
