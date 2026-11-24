namespace FactorySim.Core.Robot;

/// <summary>
/// Where one part of the robot is: its turn relative to the home pose and the point on its axis
/// it turns about. A part modelled in the home pose, with its origin on that point, is placed by
/// putting its origin at <see cref="Origin"/> and turning it by <see cref="Rotation"/>.
/// </summary>
public readonly record struct LinkFrame(Matrix3d Rotation, Vector3d Origin);
