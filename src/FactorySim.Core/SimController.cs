using System.Globalization;

namespace FactorySim.Core;

public enum SimState
{
    Stopped,
    Running,
    Paused,
}

/// <summary>Run/pause/reset state of the simulation, its clock and its time scale.</summary>
public sealed class SimController
{
    /// <summary>The time scales the simulation can run at, slowest first.</summary>
    public static readonly IReadOnlyList<double> TimeScales = new[] { 0.25, 0.5, 1.0, 2.0, 4.0 };

    private bool _stepRequested;
    private bool _stepTaken;

    public SimState State { get; private set; } = SimState.Stopped;

    /// <summary>True only while physics is advancing; this drives the "Sim running" tag.</summary>
    public bool IsRunning => State == SimState.Running;

    /// <summary>Simulation seconds per real second; one of <see cref="TimeScales"/>.</summary>
    public double TimeScale { get; private set; } = 1.0;

    /// <summary>Simulation time in seconds: the sum of the ticks the world actually ran.</summary>
    public double SimTime { get; private set; }

    /// <summary>Raised when the state actually changes.</summary>
    public event Action<SimState>? StateChanged;

    /// <summary>Raised when the time scale actually changes.</summary>
    public event Action<double>? TimeScaleChanged;

    /// <summary>Raised on every reset, so the scene can clear boxes and return actuators to rest.</summary>
    public event Action? ResetRequested;

    /// <summary>Starts from stopped, or resumes from paused.</summary>
    public void Run() => SetState(SimState.Running);

    public void Pause()
    {
        if (State == SimState.Running)
        {
            SetState(SimState.Paused);
        }
    }

    /// <summary>Stops, clears the clock and the time scale, and asks the scene to return to rest.</summary>
    public void Reset()
    {
        SetState(SimState.Stopped);
        SimTime = 0;
        _stepRequested = false;
        _stepTaken = false;
        SetTimeScale(1.0);
        ResetRequested?.Invoke();
    }

    public void SetTimeScale(double scale)
    {
        if (!TimeScales.Contains(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale,
                $"Time scale must be one of {string.Join(", ", TimeScales)}.");
        }

        if (scale == TimeScale)
        {
            return;
        }

        TimeScale = scale;
        TimeScaleChanged?.Invoke(scale);
    }

    /// <summary>Next scale up; stays at the fastest.</summary>
    public void Faster()
    {
        int i = IndexOfScale();
        if (i < TimeScales.Count - 1)
        {
            SetTimeScale(TimeScales[i + 1]);
        }
    }

    /// <summary>Next scale down; stays at the slowest.</summary>
    public void Slower()
    {
        int i = IndexOfScale();
        if (i > 0)
        {
            SetTimeScale(TimeScales[i - 1]);
        }
    }

    /// <summary>Asks for one tick while paused; does nothing in any other state.</summary>
    public void Step()
    {
        if (State == SimState.Paused)
        {
            _stepRequested = true;
        }
    }

    /// <summary>
    /// True once after <see cref="Step"/>: the scene then lets the world run for exactly one tick.
    /// </summary>
    public bool TakeStep()
    {
        if (!_stepRequested || State != SimState.Paused)
        {
            _stepRequested = false;
            return false;
        }

        _stepRequested = false;
        _stepTaken = true;
        return true;
    }

    /// <summary>
    /// Adds one tick to <see cref="SimTime"/>, but only if the world ran that tick:
    /// while running, or for the one tick of a step.
    /// </summary>
    public void Advance(double tickSeconds)
    {
        if (State == SimState.Running)
        {
            SimTime += tickSeconds;
        }
        else if (_stepTaken)
        {
            _stepTaken = false;
            SimTime += tickSeconds;
        }
    }

    /// <summary>Status-bar text when an external PLC runs on real time at another scale, else null.</summary>
    public string? TimeScaleWarning(bool linkConnected) =>
        linkConnected && TimeScale != 1.0
            ? $"Externe PLC loopt op echte tijd: timers kloppen niet bij {ScaleLabel(TimeScale)}"
            : null;

    /// <summary>"×0,25", "×1", "×4".</summary>
    public static string ScaleLabel(double scale) =>
        "×" + scale.ToString("0.##", CultureInfo.GetCultureInfo("nl-NL"));

    private int IndexOfScale()
    {
        for (int i = 0; i < TimeScales.Count; i++)
        {
            if (TimeScales[i] == TimeScale)
            {
                return i;
            }
        }

        return 2;
    }

    private void SetState(SimState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }
}
