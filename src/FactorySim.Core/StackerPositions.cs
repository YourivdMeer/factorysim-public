namespace FactorySim.Core;

/// <summary>
/// The places a stacker crane can drive to. Numbers 1 to 12 are the slots of the rack, level by
/// level from left to right (1-3 the lowest level, 10-12 the highest); 0 is the in position and
/// 13 the out position, both at the height of the roller conveyors beside the aisle. Numbers
/// 14 and 15 do not exist.
///
/// Heights are those of the top of the forks with the lift down, so the forks pass just under a
/// pallet that rests on the supports of a slot or on a roller conveyor.
/// </summary>
public sealed class StackerPositions
{
    /// <summary>Number of the in position.</summary>
    public const int In = 0;

    /// <summary>Number of the out position.</summary>
    public const int Out = 13;

    public const int Columns = 3;

    public const int Levels = 4;

    private const double EndMargin = 0.25;

    private readonly double _columnSpacing;
    private readonly double _levelHeight;
    private readonly double _firstLevelTop;
    private readonly double _transferTop;
    private readonly double _clearance;

    /// <param name="columnSpacing">Distance between the centres of two columns, in metres.</param>
    /// <param name="levelHeight">Distance between two levels, in metres.</param>
    /// <param name="firstLevelTop">Top of the supports of the lowest level.</param>
    /// <param name="transferTop">Top of the rollers of the in and out conveyors.</param>
    /// <param name="clearance">How far the lowered forks stay under the surface a pallet rests on.</param>
    public StackerPositions(double columnSpacing, double levelHeight, double firstLevelTop, double transferTop, double clearance)
    {
        if (!(columnSpacing > 0.0) || !(levelHeight > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(columnSpacing), "Spacing and level height must be positive.");
        }

        if (!(clearance >= 0.0) || !(firstLevelTop - clearance > 0.1) || !(transferTop - clearance > 0.1))
        {
            throw new ArgumentOutOfRangeException(nameof(clearance), "The forks must stay above the floor.");
        }

        _columnSpacing = columnSpacing;
        _levelHeight = levelHeight;
        _firstLevelTop = firstLevelTop;
        _transferTop = transferTop;
        _clearance = clearance;
    }

    /// <summary>Furthest the crane can drive to the left (towards column 1), in metres from the centre of column 2.</summary>
    public double MinX => ColumnX(1) - EndMargin;

    public double MaxX => ColumnX(Columns) + EndMargin;

    /// <summary>Lowest the forks can go.</summary>
    public double MinHeight => Math.Min(LevelHeight(0), LevelHeight(1)) - 0.07;

    /// <summary>Highest the forks can go.</summary>
    public double MaxHeight => LevelHeight(Levels) + 0.13;

    /// <summary>Four on/off values make a binary number; <paramref name="b1"/> is the lowest bit.</summary>
    public static int FromBits(bool b1, bool b2, bool b4, bool b8) =>
        (b1 ? 1 : 0) | (b2 ? 2 : 0) | (b4 ? 4 : 0) | (b8 ? 8 : 0);

    /// <summary>Centre of a column (1 to 3) from the centre of the middle column; left is negative.</summary>
    public double ColumnX(int column) => (column - (Columns + 1) / 2.0) * _columnSpacing;

    /// <summary>Height of the forks at a level: 0 is the roller conveyors, 1 to 4 the levels of the rack.</summary>
    public double LevelHeight(int level) =>
        level == 0 ? _transferTop - _clearance : _firstLevelTop + (level - 1) * _levelHeight - _clearance;

    /// <summary>Where the crane stands for a number; false when no such place exists.</summary>
    public bool TryGet(int number, out double x, out double height)
    {
        x = 0.0;
        height = 0.0;
        if (number == In || number == Out)
        {
            x = ColumnX(number == In ? 1 : Columns);
            height = LevelHeight(0);
            return true;
        }

        if (number < 1 || number > Columns * Levels)
        {
            return false;
        }

        x = ColumnX((number - 1) % Columns + 1);
        height = LevelHeight((number - 1) / Columns + 1);
        return true;
    }
}
