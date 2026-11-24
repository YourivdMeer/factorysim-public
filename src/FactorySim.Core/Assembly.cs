namespace FactorySim.Core;

/// <summary>
/// The rule for joining a base and a lid. A product base with a product lid on top of it becomes
/// one final product, in the colour of the base. Pick and place applies it when it sets a lid down.
/// </summary>
public static class Assembly
{
    /// <summary>How far, in metres, the middle of the lid may lie from the middle of the base.</summary>
    public const double MaxOffset = 0.03;

    private const string BasePrefix = "product_base_";
    private const string LidPrefix = "product_lid_";
    private const string FinalPrefix = "final_product_";

    /// <summary>
    /// The library id of the final product that a lid on a base makes, or null when the two do not join:
    /// the first must be a <c>product_base_*</c> and the second a <c>product_lid_*</c>.
    /// </summary>
    public static string? Result(string? baseId, string? lidId)
    {
        if (baseId is null || lidId is null
            || !baseId.StartsWith(BasePrefix, StringComparison.Ordinal) || !lidId.StartsWith(LidPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string colour = baseId[BasePrefix.Length..];
        return colour.Length == 0 ? null : FinalPrefix + colour;
    }

    /// <summary>True when the middle of the lid lies less than <see cref="MaxOffset"/> from the middle of the base, seen from above.</summary>
    public static bool Aligned(double baseX, double baseZ, double lidX, double lidZ) =>
        Math.Sqrt((lidX - baseX) * (lidX - baseX) + (lidZ - baseZ) * (lidZ - baseZ)) < MaxOffset;
}
