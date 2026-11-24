namespace FactorySim.Core.Robot;

/// <summary>
/// The robot controller as the PLC sees it. The PLC sets a position number in four bits and
/// gives a rising flank on <see cref="Go"/>; the robot moves to the taught position on its own
/// and reports <see cref="Busy"/> and <see cref="AtPosition"/>. Position 0 is always home;
/// 1 to 15 are taught in the editor. A go to a position that was not taught sets
/// <see cref="Error"/> and the arm stays put. A go while the robot moves is ignored.
///
/// <see cref="Grip"/> is passed on to the gripper in the scene; the controller only stores it.
/// </summary>
public sealed class RobotController
{
    public const int MaxPosition = 15;

    /// <summary>How close every axis must be to a position for <see cref="AtPosition"/>, in degrees.</summary>
    public const double Window = 0.1;

    private readonly RobotMotion _motion;
    private readonly Dictionary<int, RobotAngles> _poses = new();
    private bool _previousGo;

    public RobotController(RobotGeometry geometry, double speedPercent)
    {
        Geometry = geometry;
        _motion = new RobotMotion(geometry, speedPercent);
    }

    public RobotGeometry Geometry { get; }

    public bool Pos1 { get; set; }

    public bool Pos2 { get; set; }

    public bool Pos4 { get; set; }

    public bool Pos8 { get; set; }

    public bool Go { get; set; }

    public bool Grip { get; set; }

    public bool Busy => _motion.Busy;

    public bool Error { get; private set; }

    public RobotAngles Angles => _motion.Angles;

    /// <summary>The number the position bits give now.</summary>
    public int SelectedNumber => StackerPositions.FromBits(Pos1, Pos2, Pos4, Pos8);

    /// <summary>The robot stands still on the position the bits name.</summary>
    public bool AtPosition => !Busy && HasPose(SelectedNumber) && Angles.MaxDifference(Pose(SelectedNumber)) <= Window;

    /// <summary>Replaces the taught positions; numbers outside 1 to 15 are ignored, 0 is always home.</summary>
    public void SetPoses(IReadOnlyDictionary<int, RobotAngles> poses)
    {
        _poses.Clear();
        foreach ((int number, RobotAngles angles) in poses)
        {
            if (number is >= 1 and <= MaxPosition)
            {
                _poses[number] = Geometry.Clamp(angles);
            }
        }
    }

    public bool HasPose(int number) => number == 0 || _poses.ContainsKey(number);

    public RobotAngles Pose(int number) => number == 0 ? RobotAngles.Home : _poses[number];

    /// <summary>Puts the arm on a pose at once, for the editor's "go to".</summary>
    public void Jump(RobotAngles angles) => _motion.Jump(angles);

    public void Tick(double deltaSeconds)
    {
        if (Go && !_previousGo && !Busy)
        {
            int number = SelectedNumber;
            Error = !HasPose(number);
            if (!Error)
            {
                _motion.MoveTo(Pose(number));
            }
        }

        _previousGo = Go;
        _motion.Tick(deltaSeconds);
    }

    public void Reset()
    {
        Pos1 = false;
        Pos2 = false;
        Pos4 = false;
        Pos8 = false;
        Go = false;
        Grip = false;
        Error = false;
        _previousGo = false;
        _motion.Jump(RobotAngles.Home);
    }
}
