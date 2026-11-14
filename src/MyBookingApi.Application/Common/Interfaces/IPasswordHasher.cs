namespace MyBookingApi.Application.Common.Interfaces;

/// <summary>
/// Abstracts one-way password hashing so the Application layer can securely store credentials
/// without taking a dependency on a concrete hashing implementation (kept in Infrastructure).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes the supplied plaintext password using a secure, salted algorithm.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>The resulting password hash, suitable for persistence.</returns>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a plaintext password against a stored hash using a comparison that does not depend on
    /// how many leading characters match.
    /// </summary>
    /// <param name="hashedPassword">
    /// The stored hash to verify against. When it is <c>null</c> or empty - for example because no
    /// account matched the supplied email - the implementation still performs an equivalent dummy
    /// verification before returning <c>false</c>, so callers cannot leak whether an account exists
    /// through response timing (account-enumeration defense).
    /// </param>
    /// <param name="providedPassword">The plaintext password supplied by the client.</param>
    /// <returns><c>true</c> when the password matches the stored hash; otherwise <c>false</c>.</returns>
    bool VerifyPassword(string? hashedPassword, string providedPassword);
}
