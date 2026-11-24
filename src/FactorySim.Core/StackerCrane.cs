namespace FactorySim.Core;

/// <summary>
/// The logic of a stacker crane: a cart that drives along the aisle, a platform that hoists along
/// the mast, forks that reach to both sides and a small lift that raises a pallet off its support.
///
/// Two ways to command it. In target mode the PLC sets a slot number in four bits and gives a
/// <see cref="Go"/> flank; the crane drives there by itself. In jog mode the PLC drives and hoists
/// with <see cref="Left"/>, <see cref="Right"/>, <see cref="Up"/> and <see cref="Down"/> and
/// watches <see cref="AtColumn"/> and <see cref="AtLevel"/>. The forks and the lift work the same
/// in both. Drive and hoist stop while the forks are not in the middle.
///
/// The height of a target is the lowered position of the platform; the lift comes on top of it.
/// </summary>
public sealed class StackerCrane
{
    /// <summary>How close to a position the crane counts as standing on it, in metres.</summary>
    public const double Window = 0.02;

    private readonly StackerPositions _positions;
    private readonly TargetAxis _drive;
    private readonly TargetAxis _hoist;
    private readonly ForkAxis _forks;
    private readonly LinearAxis _lift;
    private bool _previousGo;

    public StackerCrane(StackerPositions positions, bool jog, double driveSpeed, double hoistSpeed,
        double forkStroke, double forkSpeed, double liftStroke, double liftSpeed)
    {
        _positions = positions;
        Jog = jog;
        _drive = new TargetAxis(positions.MinX, positions.MaxX, driveSpeed, positions.ColumnX(1));
        _hoist = new TargetAxis(positions.MinHeight, positions.MaxHeight, hoistSpeed, positions.LevelHeight(0));
        _forks = new ForkAxis(forkStroke, forkSpeed);
        _lift = new LinearAxis(liftStroke, liftSpeed);
    }

    /// <summary>True when the crane is driven by jog commands instead of a target number.</summary>
    public bool Jog { get; }

    // Commands, shared.
    public bool ForksLeft { get; set; }

    public bool ForksRight { get; set; }

    public bool Lift { get; set; }

    // Commands, target mode.
    public bool Target1 { get; set; }

    public bool Target2 { get; set; }

    public bool Target4 { get; set; }

    public bool Target8 { get; set; }

    public bool Go { get; set; }

    // Commands, jog mode.
    public bool Left { get; set; }

    public bool Right { get; set; }

    public bool Up { get; set; }

    public bool Down { get; set; }

    /// <summary>Position along the aisle, in metres from the centre of the middle column.</summary>
    public double X => _drive.Position;

    /// <summary>Height of the top of the forks with the lift down.</summary>
    public double PlatformHeight => _hoist.Position;

    /// <summary>Where the forks are: 0 in the middle, negative to the left.</summary>
    public double ForkOffset => _forks.Position;

    /// <summary>How far the lift has raised the forks.</summary>
    public double LiftOffset => _lift.Position;

    public bool ForksMiddle => _forks.AtMiddle;

    public bool ForksLeftEnd => _forks.AtLeftEnd;

    public bool ForksRightEnd => _forks.AtRightEnd;

    public bool Lifted => _lift.Position >= _lift.Stroke - PusherLogic.LimitTolerance;

    /// <summary>The crane is on its way to an accepted target.</summary>
    public bool Busy => _drive.Pending || _hoist.Pending;

    /// <summary>The crane stands still on the slot the target bits name.</summary>
    public bool AtTarget
    {
        get
        {
            int number = StackerPositions.FromBits(Target1, Target2, Target4, Target8);
            return !Busy && _positions.TryGet(number, out double x, out double height)
                && Math.Abs(X - x) <= Window && Math.Abs(PlatformHeight - height) <= Window;
        }
    }

    /// <summary>The last go named a slot that does not exist, or came with the forks out.</summary>
    public bool Error { get; private set; }

    public bool AtColumn(int column) => Math.Abs(X - _positions.ColumnX(column)) <= Window;

    public bool AtLevel(int level) => Math.Abs(PlatformHeight - _positions.LevelHeight(level)) <= Window;

    public void Tick(double deltaSeconds)
    {
        _forks.Left = ForksLeft;
        _forks.Right = ForksRight;
        _forks.Tick(deltaSeconds);
        _lift.Extend = Lift;
        _lift.Tick(deltaSeconds);

        if (Jog)
        {
            _drive.Jog = (Right ? 1 : 0) - (Left ? 1 : 0);
            _hoist.Jog = (Up ? 1 : 0) - (Down ? 1 : 0);
        }
        else if (Go && !_previousGo)
        {
            StartTrip();
        }

        _previousGo = Go;
        bool free = _forks.AtMiddle;
        _drive.Tick(deltaSeconds, free);
        _hoist.Tick(deltaSeconds, free);
    }

    public void Reset()
    {
        ForksLeft = false;
        ForksRight = false;
        Lift = false;
        Target1 = false;
        Target2 = false;
        Target4 = false;
        Target8 = false;
        Go = false;
        Left = false;
        Right = false;
        Up = false;
        Down = false;
        Error = false;
        _previousGo = false;
        _drive.Reset();
        _hoist.Reset();
        _forks.Reset();
        _lift.Reset();
    }

    private void StartTrip()
    {
        Error = false;
        int number = StackerPositions.FromBits(Target1, Target2, Target4, Target8);
        if (!_forks.AtMiddle || !_positions.TryGet(number, out double x, out double height))
        {
            Error = true;
            return;
        }

        _drive.MoveTo(x);
        _hoist.MoveTo(height);
    }
}
