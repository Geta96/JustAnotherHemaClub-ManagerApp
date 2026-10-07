using System.Security.Cryptography;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Salted, iterated password hashing (PBKDF2-SHA256) shared by every front-end.
/// The input is the uppercase-hex SHA-256 of the raw password (see
/// <see cref="AuthService.Hash"/>), so on-device stored hashes used by biometric
/// / "keep me logged in" login continue to verify after migration.
///
/// Stored format (fits the 255-char PasswordHash column):
///   $pbkdf2-sha256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "$pbkdf2-sha256$";
    private const int SaltSize = 16;          // 128-bit salt
    private const int HashSize = 32;          // 256-bit output
    private const int Iterations = 210_000;   // OWASP-aligned for PBKDF2-SHA256

    /// <summary>Hashes an already-SHA-256-pre-hashed secret into a self-describing string.</summary>
    public static string Hash(string preHashed)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            preHashed ?? string.Empty, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True when <paramref name="stored"/> is the new PBKDF2 format.</summary>
    public static bool IsPbkdf2(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Constant-time verify of a SHA-256-pre-hashed secret against a stored PBKDF2 string.</summary>
    public static bool Verify(string preHashed, string? stored)
    {
        if (!IsPbkdf2(stored)) return false;

        var parts = stored!.Split('$', StringSplitOptions.RemoveEmptyEntries);
        // parts: ["pbkdf2-sha256", iterations, salt, hash]
        if (parts.Length != 4 ||
            !int.TryParse(parts[1], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                preHashed ?? string.Empty, salt, iterations,
                HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}