namespace FactorySim.Core.Robot;

/// <summary>A rotation matrix in double precision; the columns are the turned X, Y and Z axes.</summary>
public readonly record struct Matrix3d(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    public static readonly Matrix3d Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>Rotation about X by <paramref name="degrees"/>, right-handed: Y towards Z.</summary>
    public static Matrix3d RotX(double degrees)
    {
        (double s, double c) = SinCos(degrees);
        return new Matrix3d(1, 0, 0, 0, c, -s, 0, s, c);
    }

    /// <summary>Rotation about Y (up), right-handed: Z towards X, so X goes towards -Z, like a yaw in Godot.</summary>
    public static Matrix3d RotY(double degrees)
    {
        (double s, double c) = SinCos(degrees);
        return new Matrix3d(c, 0, s, 0, 1, 0, -s, 0, c);
    }

    /// <summary>Rotation about Z, right-handed: X towards Y.</summary>
    public static Matrix3d RotZ(double degrees)
    {
        (double s, double c) = SinCos(degrees);
        return new Matrix3d(c, -s, 0, s, c, 0, 0, 0, 1);
    }

    public Vector3d ColumnX => new(M00, M10, M20);

    public Vector3d ColumnY => new(M01, M11, M21);

    public Vector3d ColumnZ => new(M02, M12, M22);

    public Matrix3d Transposed() => new(M00, M10, M20, M01, M11, M21, M02, M12, M22);

    public static Vector3d operator *(Matrix3d m, Vector3d v) => new(
        m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
        m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
        m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

    public static Matrix3d operator *(Matrix3d a, Matrix3d b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

    private static (double Sin, double Cos) SinCos(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        return (Math.Sin(radians), Math.Cos(radians));
    }
}
