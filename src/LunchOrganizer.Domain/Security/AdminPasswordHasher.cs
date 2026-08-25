using System.Security.Cryptography;

namespace LunchOrganizer.Domain.Security;

/// <summary>
/// Hashes and verifies admin passwords stored in <c>admin-users.json</c> using PBKDF2
/// (HMACSHA256, <see cref="DefaultIterations"/> iterations, a random 16-byte salt per
/// password, and a 32-byte derived key).
///
/// This replaces the previous unsalted-SHA256 ("sha256:&lt;hex&gt;") and plaintext password
/// schemes. Those schemes were weak for two independent reasons this design fixes:
/// <list type="bullet">
/// <item>No per-user salt meant identical passwords produced identical stored values, and a
/// precomputed rainbow table of SHA256(password) could recover any password whose hash leaked.
/// A random salt per password defeats precomputation because the attacker must redo the work
/// for every salt.</item>
/// <item>A naive byte-by-byte comparison of a re-computed hash against the stored hash can leak
/// timing information about how many leading bytes matched. <see cref="Verify"/> instead uses
/// <see cref="CryptographicOperations.FixedTimeEquals(System.Span{byte}, System.Span{byte})"/>,
/// which compares in constant time regardless of where (or whether) the values differ.</item>
/// </list>
///
/// Token format: a single self-describing, colon-delimited string —
/// <c>pbkdf2-sha256:&lt;iterations&gt;:&lt;saltBase64&gt;:&lt;hashBase64&gt;</c> — so the
/// iteration count and salt travel with the hash and a future increase to
/// <see cref="DefaultIterations"/> does not invalidate already-stored tokens.
/// </summary>
public static class AdminPasswordHasher
{
    /// <summary>
    /// The fixed prefix identifying a token produced by this class as PBKDF2-SHA256, i.e.
    /// <c>"pbkdf2-sha256:"</c>. A stored password value that does not start with this prefix
    /// (in the exact required token shape) is not in the supported format.
    /// </summary>
    public const string Prefix = "pbkdf2-sha256:";

    /// <summary>
    /// The number of PBKDF2 iterations used by <see cref="Hash"/> when creating a brand-new
    /// token. Existing tokens are unaffected if this value changes later, because
    /// <see cref="Verify"/> always re-derives using the iteration count recorded inside the
    /// stored token itself, not this constant.
    /// </summary>
    public const int DefaultIterations = 210_000;

    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    /// <summary>
    /// Hashes <paramref name="password"/> into a new self-describing token using a freshly
    /// generated random 16-byte salt and <see cref="DefaultIterations"/> PBKDF2 (HMACSHA256)
    /// iterations, producing a 32-byte derived key. The per-password random salt is what makes
    /// precomputed rainbow-table attacks against the resulting token infeasible.
    /// </summary>
    /// <param name="password">The plaintext password to hash. Must not be null or empty.</param>
    /// <returns>
    /// The full token: <c>pbkdf2-sha256:&lt;iterations&gt;:&lt;saltBase64&gt;:&lt;hashBase64&gt;</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="password"/> is null or empty.
    /// </exception>
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be null or empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return $"{Prefix}{DefaultIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// Checks whether <paramref name="storedPassword"/> is a well-formed
    /// <c>pbkdf2-sha256:&lt;iterations&gt;:&lt;saltBase64&gt;:&lt;hashBase64&gt;</c> token: the
    /// correct prefix, exactly four colon-delimited parts, a positive integer iteration count, a
    /// salt that decodes to exactly 16 bytes, and a hash that decodes to exactly 32 bytes. Never
    /// throws — malformed Base64 or any other parsing failure results in <c>false</c>.
    /// </summary>
    /// <param name="storedPassword">The stored password value to inspect, or null/empty.</param>
    /// <returns><c>true</c> if the value is a well-formed PBKDF2-SHA256 token; otherwise <c>false</c>.</returns>
    public static bool IsSupportedFormat(string? storedPassword)
    {
        if (string.IsNullOrEmpty(storedPassword) || !storedPassword.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = storedPassword.Split(':');
        if (parts.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);
            return salt.Length == SaltSizeBytes && hash.Length == HashSizeBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies <paramref name="suppliedPassword"/> against <paramref name="storedPassword"/>.
    /// Returns <c>false</c> (never throws) when either value is null/empty, or when
    /// <paramref name="storedPassword"/> is not a well-formed PBKDF2-SHA256 token per
    /// <see cref="IsSupportedFormat"/>. Otherwise, re-derives a 32-byte key from
    /// <paramref name="suppliedPassword"/> using the iteration count and salt recorded in the
    /// stored token, and compares it to the stored hash using
    /// <see cref="CryptographicOperations.FixedTimeEquals(System.Span{byte}, System.Span{byte})"/>
    /// so the comparison itself does not leak timing information about a partial match.
    /// </summary>
    /// <param name="suppliedPassword">The plaintext password to verify.</param>
    /// <param name="storedPassword">The stored token to verify against, or null/empty/malformed.</param>
    /// <returns><c>true</c> if the supplied password matches the stored token; otherwise <c>false</c>.</returns>
    public static bool Verify(string suppliedPassword, string? storedPassword)
    {
        if (string.IsNullOrEmpty(suppliedPassword) || !IsSupportedFormat(storedPassword))
        {
            return false;
        }

        // storedPassword is guaranteed well-formed by IsSupportedFormat at this point.
        var parts = storedPassword!.Split(':');

        // The iteration count is deliberately read from the stored token, not from
        // DefaultIterations, so that DefaultIterations can be raised in the future (for newly
        // hashed passwords) without invalidating already-stored tokens hashed with a lower count.
        var iterations = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(suppliedPassword, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
