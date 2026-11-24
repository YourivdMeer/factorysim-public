namespace FactorySim.Core.Robot;

/// <summary>The result of the forward kinematics, relative to the foot of the robot.</summary>
/// <param name="Links">The seven parts: the base and links 1 to 6.</param>
/// <param name="Wrist">The wrist centre, where axes 4, 5 and 6 meet.</param>
/// <param name="Tip">The tip of the suction cup.</param>
/// <param name="ToolDirection">Unit vector from the flange towards the tip.</param>
/// <param name="ToolRotation">The full turn of the flange relative to the home pose.</param>
public sealed record RobotFrames(
    IReadOnlyList<LinkFrame> Links,
    Vector3d Wrist,
    Vector3d Tip,
    Vector3d ToolDirection,
    Matrix3d ToolRotation);
