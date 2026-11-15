namespace MyBookingApi.Application.Common.Interfaces;

/// <summary>
/// A freshly generated refresh token: the opaque value handed to the client, the hash that must be
/// persisted in its place, and the instant at which it stops being valid.
/// </summary>
/// <param name="Token">The opaque, cryptographically random value returned to the client.</param>
/// <param name="TokenHash">The irreversible hash of <paramref name="Token"/>; only this is stored.</param>
/// <param name="ExpiresAtUtc">The instant (UTC) at which the token expires.</param>
public sealed record RefreshTokenMaterial(string Token, string TokenHash, DateTime ExpiresAtUtc);

/// <summary>
/// Abstracts the generation and hashing of refresh tokens so the Application layer never has to depend
/// on a concrete cryptographic implementation (which lives in Infrastructure).
/// </summary>
public interface IRefreshTokenGenerator
{
    /// <summary>
    /// Generates a new refresh token from a cryptographically secure random number generator, together
    /// with the hash that must be persisted for it.
    /// </summary>
    /// <returns>The token value, its hash and its expiry instant.</returns>
    RefreshTokenMaterial Generate();

    /// <summary>
    /// Computes the lookup hash for a presented refresh token so it can be matched against the stored
    /// hash without the raw value ever being persisted.
    /// </summary>
    /// <param name="token">The raw refresh token presented by the client.</param>
    /// <returns>The hash that identifies the token.</returns>
    string ComputeHash(string token);
}
