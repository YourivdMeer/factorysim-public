namespace FactorySim.Core;

/// <summary>The steps of one cycle of a machining center, in the order they run.</summary>
public enum MachiningStep
{
    Idle,
    ToEntry,
    PickDown,
    PickUp,
    ToMachine,
    LoadDown,
    LoadUp,
    Clear,
    Machining,
    Return,
    UnloadDown,
    UnloadUp,
    ToExit,
    DropDown,
    DropUp,
    Park,

    /// <summary>Back to park after <see cref="MachiningCycle.Abort"/>.</summary>
    Abort,
}

/// <summary>
/// The fixed cycle of a machining center: a robot arm takes the part at the entry, loads it
/// into the machine, steps aside while the spindle works, unloads the part and sets it down at
/// the exit. The PLC only gives the start; the station does the rest and reports busy.
///
/// The arm turns about a vertical axis. Its angle is in degrees: 0 points at the machine,
/// -entryAngle at the entry, +entryAngle at the exit and ±180 is the parking position between
/// the two. One cycle is a full turn, so the arm ends where it started.
/// </summary>
public sealed class MachiningCycle
{
    /// <summary>Arm angle at rest, in degrees.</summary>
    public const double ParkAngle = -180.0;

    /// <summary>How far the arm steps aside (towards the entry) while the spindle works, in degrees.</summary>
    public const double ClearAngle = 45.0;

    private const string RawPrefix = "raw_material_";

    private readonly double _entryAngle;
    private readonly double _swingSpeed;
    private readonly double _liftTime;
    private readonly double _machineTime;
    private readonly double _spindleTime;

    private double _time;
    private double _abortAngle;
    private double _abortLowered;

    /// <param name="entryAngle">Angle between the machine and the entry (and the exit), in degrees, below 180.</param>
    /// <param name="swingSpeed">Speed of the arm in degrees per second.</param>
    /// <param name="liftTime">Seconds the gripper takes to go down or up.</param>
    /// <param name="machineTime">Seconds the spindle works on a part.</param>
    public MachiningCycle(double entryAngle, double swingSpeed, double liftTime, double machineTime)
    {
        if (!(entryAngle > ClearAngle) || !(entryAngle < 180.0))
        {
            throw new ArgumentOutOfRangeException(nameof(entryAngle), "Entry angle must be between the clear angle and 180 degrees.");
        }

        if (!(swingSpeed > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(swingSpeed), "Swing speed must be positive.");
        }

        if (!(liftTime > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(liftTime), "Lift time must be positive.");
        }

        if (!(machineTime > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(machineTime), "Machine time must be positive.");
        }

        _entryAngle = entryAngle;
        _swingSpeed = swingSpeed;
        _liftTime = liftTime;
        _machineTime = machineTime;
        _spindleTime = Math.Min(0.5, machineTime / 3.0);
    }

    public MachiningStep Step { get; private set; } = MachiningStep.Idle;

    public bool Busy => Step != MachiningStep.Idle;

    /// <summary>Arm angle in degrees; see the class remarks.</summary>
    public double ArmAngle { get; private set; } = ParkAngle;

    /// <summary>How far the gripper is down: 0 = up, 1 = on the part.</summary>
    public double Lowered { get; private set; }

    /// <summary>How far the spindle is down: 0 = up, 1 = on the part.</summary>
    public double Spindle { get; private set; }

    /// <summary>The gripper holds the part.</summary>
    public bool Holding => Step is MachiningStep.PickUp or MachiningStep.ToMachine or MachiningStep.LoadDown
        or MachiningStep.UnloadUp or MachiningStep.ToExit or MachiningStep.DropDown;

    /// <summary>The spindle is done with the part of this cycle; true from the moment it lifts until the cycle ends.</summary>
    public bool Machined => Step is > MachiningStep.Machining and not MachiningStep.Abort
        || (Step == MachiningStep.Machining && _time >= _machineTime - _spindleTime);

    /// <summary>The arm hangs above the exit with a finished part because the exit is occupied.</summary>
    public bool WaitingForExit { get; private set; }

    /// <summary>The item a part turns into, or null when the machine leaves it as it is.</summary>
    /// <param name="item">Library id of the part, e.g. "raw_material_blue".</param>
    /// <param name="lid">True makes a lid, false a base.</param>
    public static string? Product(string? item, bool lid)
    {
        if (item is null || !item.StartsWith(RawPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return (lid ? "product_lid_" : "product_base_") + item[RawPrefix.Length..];
    }

    /// <param name="deltaSeconds">Length of the step.</param>
    /// <param name="start">Start command; a cycle begins when it is on, the station is idle and a part is at the entry.</param>
    /// <param name="partAtEntry">A part lies at the pick position.</param>
    /// <param name="exitClear">The place where the finished part is set down is free.</param>
    public void Tick(double deltaSeconds, bool start, bool partAtEntry, bool exitClear)
    {
        if (Step == MachiningStep.Idle)
        {
            if (start && partAtEntry)
            {
                Step = MachiningStep.ToEntry;
                _time = 0.0;
            }

            return;
        }

        _time += deltaSeconds;
        WaitingForExit = false;
        while (Step != MachiningStep.Idle && _time >= Duration(Step))
        {
            if (Step == MachiningStep.ToExit && !exitClear)
            {
                _time = Duration(Step);
                WaitingForExit = true;
                break;
            }

            _time -= Duration(Step);
            Step = Step is MachiningStep.Park or MachiningStep.Abort ? MachiningStep.Idle : Step + 1;
        }

        UpdatePose();
    }

    /// <summary>Gives up the cycle (no part where one was expected) and returns the arm to park.</summary>
    public void Abort()
    {
        if (Step == MachiningStep.Idle)
        {
            return;
        }

        _abortAngle = ArmAngle;
        _abortLowered = Lowered;
        Step = MachiningStep.Abort;
        _time = 0.0;
        WaitingForExit = false;
        Spindle = 0.0;
    }

    public void Reset()
    {
        Step = MachiningStep.Idle;
        _time = 0.0;
        WaitingForExit = false;
        UpdatePose();
    }

    private double Duration(MachiningStep step) => step switch
    {
        MachiningStep.ToEntry => (180.0 - _entryAngle) / _swingSpeed,
        MachiningStep.ToMachine or MachiningStep.ToExit => _entryAngle / _swingSpeed,
        MachiningStep.Clear or MachiningStep.Return => ClearAngle / _swingSpeed,
        MachiningStep.Park => (180.0 - _entryAngle) / _swingSpeed,
        MachiningStep.Machining => _machineTime,
        // Swing back the short way and lift, whichever takes longer.
        MachiningStep.Abort => Math.Max(Math.Abs(ParkAngle - _abortAngle) / _swingSpeed, _abortLowered * _liftTime) + 1e-9,
        _ => _liftTime,
    };

    private void UpdatePose()
    {
        double f = Step == MachiningStep.Idle ? 0.0 : Math.Clamp(_time / Duration(Step), 0.0, 1.0);
        double e = _entryAngle;
        (double from, double to) = Step switch
        {
            MachiningStep.Idle => (ParkAngle, ParkAngle),
            MachiningStep.ToEntry => (ParkAngle, -e),
            MachiningStep.PickDown or MachiningStep.PickUp => (-e, -e),
            MachiningStep.ToMachine => (-e, 0.0),
            MachiningStep.Clear => (0.0, -ClearAngle),
            MachiningStep.Machining => (-ClearAngle, -ClearAngle),
            MachiningStep.Return => (-ClearAngle, 0.0),
            MachiningStep.ToExit => (0.0, e),
            MachiningStep.DropDown or MachiningStep.DropUp => (e, e),
            MachiningStep.Park => (e, 180.0),
            MachiningStep.Abort => (_abortAngle, ParkAngle),
            _ => (0.0, 0.0),        // loading and unloading at the machine
        };
        ArmAngle = from + (to - from) * f;

        Lowered = Step switch
        {
            MachiningStep.PickDown or MachiningStep.LoadDown or MachiningStep.UnloadDown or MachiningStep.DropDown => f,
            MachiningStep.PickUp or MachiningStep.LoadUp or MachiningStep.UnloadUp or MachiningStep.DropUp => 1.0 - f,
            MachiningStep.Abort => _abortLowered * (1.0 - f),
            _ => 0.0,
        };

        Spindle = Step == MachiningStep.Machining
            ? Math.Clamp(Math.Min(_time, _machineTime - _time) / _spindleTime, 0.0, 1.0)
            : 0.0;
    }
}
