namespace FactorySim.Core.Plc.TestPlc;

/// <summary>
/// The scan of the sorter in <c>tools/fake_plc/fake_plc.py</c>, line for line: start/stop latch, push high boxes
/// aside, a 3 s watchdog on the pusher that only runs while the simulation runs. The offsets are those of the
/// MVP scene (<c>tagmap.json</c>).
/// </summary>
public sealed class SorterProgram
{
    public const int StartCoil = 0, StopCoil = 1, AtPusherCoil = 2, HighBoxCoil = 3, PusherBackCoil = 4, PusherFrontCoil = 5, SimRunningCoil = 6;
    public const int ConveyorOutput = 0, PusherOutput = 1, EmitterOutput = 2, RunLampOutput = 3;
    public const int OutputCount = 4;
    public const double PushTimeoutSeconds = 3.0;

    private double? _pushStarted;

    public bool Run { get; private set; }

    /// <summary>0 = transport, 10 = push, 20 = return.</summary>
    public int Step { get; private set; }

    public bool Fault { get; private set; }

    public int SortedCount { get; private set; }

    /// <param name="coils">The inputs of the PLC (at least 7).</param>
    /// <param name="now">Seconds on a monotonic clock.</param>
    /// <returns>The four outputs: conveyor, pusher, emitter, run lamp.</returns>
    public bool[] Scan(IReadOnlyList<bool> coils, double now)
    {
        if (coils[StopCoil] || !coils[SimRunningCoil] || Fault)
        {
            Run = false;
        }
        else if (coils[StartCoil])
        {
            Run = true;
        }

        if (Fault && coils[StartCoil] && !coils[StopCoil])
        {
            Fault = false;
            Step = 0;
        }

        switch (Step)
        {
            case 0 when Run && coils[AtPusherCoil] && coils[HighBoxCoil] && coils[PusherBackCoil]:
                Step = 10;
                break;
            case 10 when coils[PusherFrontCoil]:
                SortedCount++;
                Step = 20;
                break;
            case 20 when coils[PusherBackCoil]:
                Step = 0;
                break;
        }

        // Watchdog, like a TON over steps 10 and 20; a paused scene does not trip it.
        if (Step is 10 or 20 && coils[SimRunningCoil])
        {
            if (_pushStarted is null)
            {
                _pushStarted = now;
            }
            else if (now - _pushStarted > PushTimeoutSeconds)
            {
                Fault = true;
                Step = 0;
                _pushStarted = null;
            }
        }
        else
        {
            _pushStarted = null;
        }

        var outputs = new bool[OutputCount];
        outputs[ConveyorOutput] = Run && Step == 0;
        outputs[PusherOutput] = Step == 10;
        outputs[EmitterOutput] = Run;
        outputs[RunLampOutput] = Run;
        return outputs;
    }
}
