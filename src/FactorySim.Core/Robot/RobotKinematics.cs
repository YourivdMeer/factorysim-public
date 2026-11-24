namespace FactorySim.Core.Robot;

/// <summary>
/// Forward and inverse kinematics of the six-axis arm described by <see cref="RobotGeometry"/>.
/// The same functions place the parts in the scene and are checked by the unit tests.
///
/// Conventions, all relative to the foot, in the axes of Godot (X, Y up, Z):
/// <list type="bullet">
/// <item>A1 turns about +Y; positive takes +X towards -Z, like the yaw of an item.</item>
/// <item>A2 positive leans the upper arm forward (from +Y towards +X at A1 = 0).</item>
/// <item>A3 positive lowers the forearm; at A3 = 0 the forearm is at right angles to the upper arm.</item>
/// <item>A4 turns the forearm about its own length.</item>
/// <item>A5 bends the wrist; at A5 = 0 the tool is at right angles to the forearm, so it points
///   down at home. Positive bends the tool back towards the robot, negative forward.</item>
/// <item>A6 turns the flange about the tool direction.</item>
/// </list>
/// </summary>
public static class RobotKinematics
{
    /// <summary>The base plus links 1 to 6.</summary>
    public const int LinkCount = 7;

    /// <summary>Names of the parts in the model, in the order of <see cref="RobotFrames.Links"/>.</summary>
    public static readonly IReadOnlyList<string> PartNames = new[]
    {
        "Base", "Link_1", "Link_2", "Link_3", "Link_4", "Link_5", "Link_6",
    };

    private static readonly Matrix3d WristHome = Matrix3d.RotZ(-90.0);

    /// <summary>The point each part turns about, with the robot at home; the origins of the parts in the model.</summary>
    public static IReadOnlyList<Vector3d> HomeJoints(RobotGeometry g) =>
        Forward(g, RobotAngles.Home).Links.Select(link => link.Origin).ToArray();

    public static RobotFrames Forward(RobotGeometry g, RobotAngles a)
    {
        Matrix3d r1 = Matrix3d.RotY(a.A1);
        Matrix3d r2 = r1 * Matrix3d.RotZ(-a.A2);
        Matrix3d r3 = r2 * Matrix3d.RotZ(-a.A3);
        Matrix3d r4 = r3 * Matrix3d.RotX(a.A4);
        Matrix3d r5 = r4 * Matrix3d.RotZ(-(a.A5 + 90.0));
        Matrix3d r6 = r5 * Matrix3d.RotX(a.A6);

        var p2 = new Vector3d(0.0, g.BaseHeight, 0.0);
        Vector3d p3 = p2 + r2 * new Vector3d(0.0, g.UpperArm, 0.0);
        Vector3d p4 = p3 + r3 * new Vector3d(0.0, g.ElbowOffset, 0.0);
        Vector3d wrist = p4 + r3 * new Vector3d(g.Forearm, 0.0, 0.0);
        Vector3d tool = r6.ColumnX;
        Vector3d flange = wrist + tool * g.WristToFlange;
        Vector3d tip = flange + tool * g.ToolLength;

        Matrix3d wristBack = WristHome.Transposed();
        var links = new[]
        {
            new LinkFrame(Matrix3d.Identity, Vector3d.Zero),
            new LinkFrame(r1, Vector3d.Zero),
            new LinkFrame(r2, p2),
            new LinkFrame(r3, p3),
            new LinkFrame(r4, p4),
            new LinkFrame(r5 * wristBack, wrist),
            new LinkFrame(r6 * wristBack, flange),
        };
        return new RobotFrames(links, wrist, tip, tool, r6 * wristBack);
    }

    /// <summary>
    /// Angles that put the tip on <paramref name="target"/>; null when the point is out of reach or
    /// only reachable outside the axis ranges. With <paramref name="toolDown"/> the tool points straight
    /// down; otherwise it keeps the direction it has in <paramref name="seed"/>. Of the possible poses this
    /// one has the elbow up and the arm facing the point; the wrist takes the solution closest to the
    /// seed. Axis 6 keeps its angle from the seed.
    /// </summary>
    public static RobotAngles? Solve(RobotGeometry g, Vector3d target, bool toolDown, RobotAngles seed)
    {
        Vector3d direction = toolDown ? Vector3d.Down : Forward(g, seed).ToolDirection.Normalized();
        Vector3d wrist = target - direction * (g.WristToFlange + g.ToolLength);

        // Axis 1 faces the wrist; a wrist straight above the foot leaves it where it was.
        double r = Math.Sqrt(wrist.X * wrist.X + wrist.Z * wrist.Z);
        double a1 = r < 1e-9 ? seed.A1 : Degrees(Math.Atan2(-wrist.Z, wrist.X));
        double h = wrist.Y - g.BaseHeight;

        // Axes 2 and 3: two links in the plane of the arm. The second one runs from axis 3 to the
        // wrist and is tilted up by the elbow offset.
        double l2 = g.UpperArm;
        double l3 = Math.Sqrt(g.ElbowOffset * g.ElbowOffset + g.Forearm * g.Forearm);
        double beta = Math.Atan2(g.ElbowOffset, g.Forearm);
        double cosine = (r * r + h * h - l2 * l2 - l3 * l3) / (2.0 * l2 * l3);
        if (cosine > 1.0 + 1e-12 || cosine < -1.0 - 1e-12)
        {
            return null;
        }

        double gamma = -Math.Acos(Math.Clamp(cosine, -1.0, 1.0));          // elbow up
        double upper = Math.Atan2(h, r) - Math.Atan2(l3 * Math.Sin(gamma), l2 + l3 * Math.Cos(gamma));
        double a2 = 90.0 - Degrees(upper);
        double pitch = beta - (upper + gamma);                               // A2 + A3, in radians
        double a3 = Degrees(pitch) - a2;

        // Axes 4 and 5: the tool direction seen from the forearm.
        Matrix3d r3 = Matrix3d.RotY(a1) * Matrix3d.RotZ(-(a2 + a3));
        Vector3d t = r3.Transposed() * direction;
        double a5 = Degrees(Math.Asin(Math.Clamp(-t.X, -1.0, 1.0)));
        double sideways = Math.Sqrt(t.Y * t.Y + t.Z * t.Z);
        double a4 = sideways < 1e-9 ? seed.A4 : Degrees(Math.Atan2(-t.Z, -t.Y));

        RobotAngles? best = null;
        foreach ((double c4, double c5) in new[] { (a4, a5), (Wrap(a4 + 180.0), Wrap(180.0 - a5)) })
        {
            var candidate = new RobotAngles(a1, a2, a3, c4, c5, seed.A6);
            if (!g.WithinLimits(candidate))
            {
                continue;
            }

            if (best is null || WristDistance(candidate, seed) < WristDistance(best.Value, seed))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double WristDistance(RobotAngles a, RobotAngles seed) => Math.Abs(a.A4 - seed.A4) + Math.Abs(a.A5 - seed.A5);

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    private static double Wrap(double degrees)
    {
        double value = degrees % 360.0;
        if (value > 180.0)
        {
            value -= 360.0;
        }
        else if (value < -180.0)
        {
            value += 360.0;
        }

        return value;
    }
}
