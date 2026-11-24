namespace FactorySim.Core.Robot;

/// <summary>
/// Sizes and limits of a six-axis robot arm. The default follows the public specification of a
/// small industrial arm (reach 0.58 m, 3 kg); the model in the library is an own design.
///
/// The arm stands on its foot at the origin, in the axes of Godot (X, Y up, Z). Axis 1 turns
/// about Y; at A1 = 0 the arm points along +X. Axes 2 and 3 tilt the upper arm and the forearm
/// in the plane of the arm, axis 4 turns the forearm about its length, axis 5 bends the wrist
/// and axis 6 turns the flange.
/// </summary>
/// <param name="BaseHeight">Height of axis 2 above the foot.</param>
/// <param name="UpperArm">Axis 2 to axis 3.</param>
/// <param name="ElbowOffset">The kink at the elbow: how far the forearm axis lies above axis 3.</param>
/// <param name="Forearm">Axis 3 to the wrist centre, along the forearm.</param>
/// <param name="WristToFlange">Wrist centre to the flange.</param>
/// <param name="ToolLength">Flange to the tip of the suction cup.</param>
public sealed record RobotGeometry(
    double BaseHeight,
    double UpperArm,
    double ElbowOffset,
    double Forearm,
    double WristToFlange,
    double ToolLength)
{
    /// <summary>Lower limit of each axis, in degrees.</summary>
    public IReadOnlyList<double> Min { get; init; } = new[] { -165.0, -110.0, -110.0, -160.0, -120.0, -400.0 };

    /// <summary>Upper limit of each axis, in degrees.</summary>
    public IReadOnlyList<double> Max { get; init; } = new[] { 165.0, 110.0, 70.0, 160.0, 120.0, 400.0 };

    /// <summary>Top speed of each axis, in degrees per second.</summary>
    public IReadOnlyList<double> MaxSpeed { get; init; } = new[] { 250.0, 250.0, 250.0, 320.0, 320.0, 420.0 };

    public static RobotGeometry Default { get; } = new(0.290, 0.270, 0.070, 0.302, 0.072, 0.050);

    /// <summary>Names of the sizes in the params of the catalogue.</summary>
    public static readonly IReadOnlyList<string> ParamNames = new[]
    {
        "baseHeight", "upperArm", "elbowOffset", "forearm", "wristToFlange", "toolLength",
    };

    /// <summary>Distance from axis 2 to the wrist centre with the arm stretched.</summary>
    public double Reach => UpperArm + Math.Sqrt(ElbowOffset * ElbowOffset + Forearm * Forearm);

    /// <summary>Reads the sizes from parameters (the catalogue); missing ones keep the default.</summary>
    public static RobotGeometry FromParams(Func<string, double, double> param)
    {
        RobotGeometry d = Default;
        return new RobotGeometry(
            param("baseHeight", d.BaseHeight),
            param("upperArm", d.UpperArm),
            param("elbowOffset", d.ElbowOffset),
            param("forearm", d.Forearm),
            param("wristToFlange", d.WristToFlange),
            param("toolLength", d.ToolLength));
    }

    public bool WithinLimits(RobotAngles angles)
    {
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            if (angles[axis] < Min[axis] - 1e-9 || angles[axis] > Max[axis] + 1e-9)
            {
                return false;
            }
        }

        return true;
    }

    public RobotAngles Clamp(RobotAngles angles)
    {
        var values = new double[RobotAngles.Count];
        for (int axis = 0; axis < RobotAngles.Count; axis++)
        {
            values[axis] = Math.Clamp(angles[axis], Min[axis], Max[axis]);
        }

        return RobotAngles.FromArray(values);
    }
}
