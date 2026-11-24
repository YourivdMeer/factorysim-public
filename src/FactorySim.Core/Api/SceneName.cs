namespace FactorySim.Core.Api;

/// <summary>Scene names the test API accepts: a file name without folder or extension, never a path.</summary>
public static class SceneName
{
    public const int MaxLength = 100;

    /// <summary>
    /// True for letters, digits, space, '_', '-' and '.', not starting with a dot or a space and without "..".
    /// That rules out every path: no separators, no drive, no parent folder.
    /// </summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength || name[0] is '.' or ' ' || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.');
    }
}
