namespace FactorySim.Core;

/// <summary>
/// A round tank with a fill valve and a discharge valve. The level rises with the inflow
/// (maximum inflow × fill valve) and falls with an outflow that, like a real tank draining
/// through an opening, grows with the square root of the level:
/// outflow = maximum outflow × discharge valve × √(level / height).
/// The level stays between 0 and the height; inflow above a full tank runs over.
/// </summary>
public sealed class TankLogic
{
    private readonly double _area;

    /// <param name="height">Height of the liquid column when full, in m.</param>
    /// <param name="diameter">Inner diameter, in m.</param>
    /// <param name="maxInflow">Inflow with the fill valve fully open, in m³/s.</param>
    /// <param name="maxOutflow">Outflow of a full tank with the discharge valve fully open, in m³/s.</param>
    public TankLogic(double height, double diameter, double maxInflow, double maxOutflow)
    {
        if (!(height > 0) || !(diameter > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(height), "A tank needs a height and a diameter above zero.");
        }

        Height = height;
        MaxInflow = Math.Max(0.0, maxInflow);
        MaxOutflow = Math.Max(0.0, maxOutflow);
        _area = Math.PI * diameter * diameter / 4.0;
    }

    public double Height { get; }

    public double MaxInflow { get; }

    public double MaxOutflow { get; }

    /// <summary>Height of the liquid, in m.</summary>
    public double Level { get; private set; }

    /// <summary>Inflow of the last step, in m³/s.</summary>
    public double Inflow { get; private set; }

    /// <summary>Outflow of the last step, in m³/s.</summary>
    public double Outflow { get; private set; }

    /// <summary>True when the last step had more coming in than the full tank could hold.</summary>
    public bool Overflowing { get; private set; }

    /// <summary>Level as a fraction of the height, 0 to 1.</summary>
    public double Fraction => Level / Height;

    /// <summary>Advances the tank.</summary>
    /// <param name="fill">Fill valve, 0 (closed) to 1 (open).</param>
    /// <param name="discharge">Discharge valve, 0 (closed) to 1 (open).</param>
    public void Step(double delta, double fill, double discharge)
    {
        Inflow = MaxInflow * Math.Clamp(fill, 0.0, 1.0);
        Outflow = MaxOutflow * Math.Clamp(discharge, 0.0, 1.0) * Math.Sqrt(Level / Height);

        // Never drain more than is in the tank within one step.
        Outflow = Math.Min(Outflow, Level * _area / delta + Inflow);
        double level = Level + (Inflow - Outflow) * delta / _area;
        Overflowing = level > Height;
        Level = Math.Clamp(level, 0.0, Height);
    }

    /// <summary>Sets the level directly, for a test or a level that starts filled.</summary>
    public void SetLevel(double level) => Level = Math.Clamp(level, 0.0, Height);

    public void Reset()
    {
        Level = 0.0;
        Inflow = 0.0;
        Outflow = 0.0;
        Overflowing = false;
    }
}
