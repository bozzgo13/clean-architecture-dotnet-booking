using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Common.Interfaces;

/// <summary>
/// A signed access token together with the instant (UTC) at which it stops being valid.
/// </summary>
/// <param name="Token">The serialized, signed JWT.</param>
/// <param name="ExpiresAtUtc">The expiry instant, derived from the token's <c>exp</c> claim.</param>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Abstracts issuance of signed JWT access tokens so the Application layer can authenticate users
/// without taking a dependency on a concrete token library (kept in Infrastructure).
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Generates a signed access token that identifies the supplied user and carries their role, so
    /// downstream authorization decisions (including role checks) can be made from the token alone.
    /// </summary>
    /// <param name="user">The authenticated user the token is issued for.</param>
    /// <returns>The signed token and its expiry instant (UTC).</returns>
    AccessToken GenerateAccessToken(User user);
}
