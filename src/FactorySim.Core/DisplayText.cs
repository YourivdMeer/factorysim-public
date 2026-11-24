using System.Globalization;

namespace FactorySim.Core;

/// <summary>What a digital display shows for the number the PLC writes.</summary>
public static class DisplayText
{
    public const int MaxDecimals = 4;

    /// <summary>The number with a fixed number of decimals and a decimal comma: 1234 with 2 decimals is "12,34".</summary>
    public static string Format(int number, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, MaxDecimals);
        if (decimals == 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        decimal value = number / (decimal)Math.Pow(10, decimals);
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
