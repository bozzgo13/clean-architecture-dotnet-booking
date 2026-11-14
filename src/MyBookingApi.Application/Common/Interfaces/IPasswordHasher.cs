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
}
