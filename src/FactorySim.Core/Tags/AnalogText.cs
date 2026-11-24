using System.Globalization;

namespace FactorySim.Core.Tags;

/// <summary>How the tag table shows the value of an Int tag.</summary>
public static class AnalogText
{
    /// <summary>
    /// "1,23 m  (123)": the value in the simulation with a decimal comma and its unit, followed by the
    /// number on the line when the scale is not 1. The scale decides the number of decimals (100 gives two).
    /// </summary>
    public static string Format(int number, double scale, string? unit)
    {
        string unitText = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        if (scale == 1 || !(scale > 0))
        {
            return number.ToString(CultureInfo.InvariantCulture) + unitText;
        }

        int decimals = Math.Max(0, (int)Math.Ceiling(Math.Log10(scale) - 1e-9));
        string value = (number / scale).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            .Replace('.', ',');
        return $"{value}{unitText}  ({number.ToString(CultureInfo.InvariantCulture)})";
    }
}
