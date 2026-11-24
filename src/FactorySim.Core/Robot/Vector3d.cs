using System.Globalization;

namespace FactorySim.Core.Robot;

/// <summary>A point or direction in double precision, in the axes of Godot: X, Y up, Z.</summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static readonly Vector3d Zero = new(0.0, 0.0, 0.0);

    public static readonly Vector3d Up = new(0.0, 1.0, 0.0);

    public static readonly Vector3d Down = new(0.0, -1.0, 0.0);

    public double Length => Math.Sqrt(Dot(this));

    public double Dot(Vector3d other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vector3d Normalized()
    {
        double length = Length;
        return length > 0.0 ? this / length : this;
    }

    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);

    public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vector3d operator *(double s, Vector3d a) => a * s;

    public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####})", X, Y, Z);
}
