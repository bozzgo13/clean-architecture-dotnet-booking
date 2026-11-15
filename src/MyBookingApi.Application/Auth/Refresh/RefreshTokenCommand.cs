using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Auth.Common;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Auth.Commands.Refresh;

/// <summary>
/// Represents a CQRS Command that exchanges a valid refresh token for a brand-new access token and a
/// freshly rotated refresh token, allowing a client to keep a session alive without re-sending the
/// password.
/// </summary>
/// <param name="RefreshToken">The opaque refresh token previously issued to the client. Never persisted.</param>
public record RefreshTokenCommand(
    string RefreshToken
) : IRequest<AuthTokenResponse>;

/// <summary>
/// Handles the execution of the <see cref="RefreshTokenCommand"/>. It looks the presented token up by
/// its irreversible hash, rejects it when it is unknown, expired or already revoked, and otherwise
/// rotates it: the presented token is revoked and linked to its successor, a new refresh token is
/// issued and persisted, and a new access token is minted for the owning account.
///
/// Replaying an already-revoked token is treated as a possible token-theft attempt: every active
/// refresh token belonging to that user is revoked to contain the breach.
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="jwtTokenGenerator">The service that issues the signed JWT access token.</param>
    /// <param name="refreshTokenGenerator">The service that generates and hashes refresh tokens.</param>
    /// <param name="logger">The structured logger used for security-relevant audit events.</param>
    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenGenerator refreshTokenGenerator,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenGenerator = refreshTokenGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Rotates the presented refresh token asynchronously.
    /// </summary>
    /// <param name="request">The command carrying the refresh token submitted by the client.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The newly issued access and refresh tokens on success.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the token is unknown, expired or revoked. The same generic exception is raised in
    /// every case so the response cannot be used to distinguish the failure reason; the global
    /// exception handler maps it to a generic 401 Unauthorized.
    /// </exception>
    public async Task<AuthTokenResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Only the hash is ever stored, so the presented value is hashed for the lookup. The raw token
        // is never compared or persisted, and is never written to the log.
        var presentedHash = _refreshTokenGenerator.ComputeHash(request.RefreshToken);

        // Tracked (not AsNoTracking) because a successful redemption mutates the entity to revoke it.
        var storedToken = await _context.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

        if (storedToken is null)
        {
            _logger.LogWarning("Refresh rejected: the presented token is not recognised.");
            throw new UnauthorizedAccessException("The refresh token is invalid or has expired.");
        }

        if (storedToken.IsRevoked)
        {
            // A revoked token can never be redeemed again. Its replay strongly suggests that the token
            // was stolen, so - defensively - every active token for the account is revoked as well.
            await RevokeAllActiveTokensAsync(storedToken.UserId, cancellationToken);

            _logger.LogWarning(
                "Refresh rejected: a revoked token was replayed. All active tokens for UserId={UserId} were revoked as a precaution.",
                storedToken.UserId);

            throw new UnauthorizedAccessException("The refresh token is invalid or has expired.");
        }

        if (storedToken.IsExpired)
        {
            _logger.LogWarning("Refresh rejected: the presented token has expired.");
            throw new UnauthorizedAccessException("The refresh token is invalid or has expired.");
        }

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == storedToken.UserId, cancellationToken);

        if (user is null)
        {
            // The account no longer exists (e.g. it was deleted). No token may be issued for it.
            _logger.LogWarning("Refresh rejected: the account for UserId={UserId} no longer exists.", storedToken.UserId);
            throw new UnauthorizedAccessException("The refresh token is invalid or has expired.");
        }

        var now = DateTime.UtcNow;

        // Rotation: issue the successor first so the presented token can record which token replaced it.
        var newRefreshToken = _refreshTokenGenerator.Generate();

        storedToken.Revoke(now, newRefreshToken.TokenHash);

        _context.RefreshTokens.Add(
            new RefreshToken(user.Id, newRefreshToken.TokenHash, now, newRefreshToken.ExpiresAtUtc));

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);

        await SaveChangesRejectingConcurrencyAsync(cancellationToken);

        // Audit-relevant security event (recorded at Information). Only the opaque user identifier is
        // recorded; no token value is ever logged.
        _logger.LogInformation("Refresh succeeded. UserId={UserId}", user.Id);

        return new AuthTokenResponse(accessToken.Token, accessToken.ExpiresAtUtc, newRefreshToken.Token);
    }

    /// <summary>
    /// Revokes every refresh token that is still active for the supplied user. Used when a revoked
    /// token is replayed, to invalidate any token an attacker may have obtained.
    /// </summary>
    /// <param name="userId">The identifier of the user whose tokens should be revoked.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    private async Task RevokeAllActiveTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.Revoke(now);
        }

        await SaveChangesRejectingConcurrencyAsync(cancellationToken);
    }

    /// <summary>
    /// Persists pending changes, translating a lost optimistic-concurrency race into the same generic
    /// <see cref="UnauthorizedAccessException"/> as an invalid token. When two requests redeem the
    /// same refresh token at once, the loser of the race must be rejected rather than allowed to mint
    /// a second live token from the one presented.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when the row was modified by a concurrent request.</exception>
    private async Task SaveChangesRejectingConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Refresh rejected: the presented token was redeemed by a concurrent request.");

            throw new UnauthorizedAccessException("The refresh token is invalid or has expired.");
        }
    }
}
