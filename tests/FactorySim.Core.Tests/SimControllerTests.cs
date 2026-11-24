using FactorySim.Core;

namespace FactorySim.Core.Tests;

public class SimControllerTests
{
    [Fact]
    public void Starts_stopped()
    {
        var sim = new SimController();

        Assert.Equal(SimState.Stopped, sim.State);
        Assert.False(sim.IsRunning);
    }

    [Fact]
    public void Run_pause_resume()
    {
        var sim = new SimController();

        sim.Run();
        Assert.Equal(SimState.Running, sim.State);
        Assert.True(sim.IsRunning);

        sim.Pause();
        Assert.Equal(SimState.Paused, sim.State);
        Assert.False(sim.IsRunning);

        sim.Run();
        Assert.Equal(SimState.Running, sim.State);
    }

    [Fact]
    public void Pause_while_stopped_does_nothing()
    {
        var sim = new SimController();

        sim.Pause();

        Assert.Equal(SimState.Stopped, sim.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_stops_and_notifies_from_running_or_paused(bool pauseFirst)
    {
        var sim = new SimController();
        int resets = 0;
        sim.ResetRequested += () => resets++;
        sim.Run();
        if (pauseFirst)
        {
            sim.Pause();
        }

        sim.Reset();

        Assert.Equal(SimState.Stopped, sim.State);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void State_changes_are_reported_once_per_change()
    {
        var sim = new SimController();
        var seen = new List<SimState>();
        sim.StateChanged += seen.Add;

        sim.Run();
        sim.Run();
        sim.Pause();
        sim.Reset();

        Assert.Equal(new[] { SimState.Running, SimState.Paused, SimState.Stopped }, seen);
    }
}
