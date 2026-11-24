namespace FactorySim.Core.Scenes;

/// <summary>The size of the letters on a sign.</summary>
public enum SignSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// The text of a sign (plan 19): at most <see cref="MaxLength"/> characters on at most
/// <see cref="MaxLines"/> lines, with plain line breaks. Empty or only space means no text.
/// </summary>
public static class SignText
{
    public const int MaxLength = 80;
    public const int MaxLines = 3;

    /// <summary>Key in the item's params for the letter size: 0 small, 1 medium, 2 large.</summary>
    public const string SizeParam = "size";

    /// <summary>The text as it is stored, or null for no text.</summary>
    public static string? Normalize(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');
        string result = string.Join("\n", lines.Take(MaxLines).Select(line => line.TrimEnd()));
        if (result.Length > MaxLength)
        {
            result = result[..MaxLength].TrimEnd();
        }

        return result.Length == 0 ? null : result;
    }

    /// <summary>The letter size for a value of the size setting; anything out of range goes to the nearest size.</summary>
    public static SignSize SizeOf(double value) => value switch
    {
        < 0.5 => SignSize.Small,
        < 1.5 => SignSize.Medium,
        _ => SignSize.Large,
    };
}
