using System.Security.Cryptography;
using System.Text;

namespace FactorySim.Core.Api;

/// <summary>The secret a script sends in <see cref="HeaderName"/>; a new one every time the API starts.</summary>
public static class ApiToken
{
    public const string HeaderName = "X-FactorySim-Token";

    public const int Length = 32;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>32 random characters from a cryptographic generator.</summary>
    public static string New()
    {
        var chars = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Compares in constant time, so the answer time says nothing about the token.</summary>
    public static bool Matches(string expected, string? given)
    {
        if (given is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
    }
}
