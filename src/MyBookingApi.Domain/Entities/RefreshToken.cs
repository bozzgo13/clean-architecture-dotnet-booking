using MyBookingApi.Domain.Common;

namespace MyBookingApi.Domain.Entities;

/// <summary>
/// A persisted refresh token that lets a client obtain a new access token without re-sending the
/// password. The raw token value is never stored: only its irreversible hash is kept, so a leaked
/// database dump cannot be replayed against the API.
///
/// A refresh token follows a strict single-use lifecycle: it is either active, or permanently
/// revoked (because it was rotated, or because a breach was detected), or expired.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>The identifier of the user the token was issued to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The hash of the opaque token value handed to the client. Never the raw value itself.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>The instant (UTC) at which the token was issued.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The instant (UTC) after which the token is no longer accepted.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>The instant (UTC) at which the token was revoked, or <c>null</c> while it is active.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>
    /// The hash of the token that superseded this one during rotation, or <c>null</c>. It keeps an audit
    /// trail of the rotation chain without ever persisting a usable token value.
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    // EF Core needs a parameterless constructor.
    protected RefreshToken() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshToken"/> class.
    /// </summary>
    /// <param name="userId">The identifier of the user the token belongs to.</param>
    /// <param name="tokenHash">The hash of the token value that will be given to the client.</param>
    /// <param name="createdAtUtc">The instant (UTC) at which the token is issued.</param>
    /// <param name="expiresAtUtc">The instant (UTC) at which the token expires. Must be after <paramref name="createdAtUtc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when an argument violates a domain invariant.</exception>
    public RefreshToken(Guid userId, string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash cannot be empty.", nameof(tokenHash));

        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Expiry must be later than the creation instant.", nameof(expiresAtUtc));

        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Gets a value indicating whether the token's lifetime has elapsed.</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;

    /// <summary>Gets a value indicating whether the token has been revoked.</summary>
    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Gets a value indicating whether the token may still be redeemed (neither revoked nor expired).</summary>
    public bool IsActive => !IsRevoked && !IsExpired;

    /// <summary>
    /// Revokes the token, optionally recording the hash of the token that replaced it. Revocation is
    /// final: a revoked token can never become active again.
    /// </summary>
    /// <param name="revokedAtUtc">The instant (UTC) at which the token is being revoked.</param>
    /// <param name="replacedByTokenHash">The hash of the rotating token, when this revocation is part of a rotation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the token has already been revoked.</exception>
    public void Revoke(DateTime revokedAtUtc, string? replacedByTokenHash = null)
    {
        if (IsRevoked)
            throw new InvalidOperationException("The refresh token has already been revoked.");

        RevokedAtUtc = revokedAtUtc;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
