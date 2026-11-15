using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Common.Interfaces;

namespace MyBookingApi.Application.Auth.Commands.Logout;

/// <summary>
/// Represents a CQRS Command that ends a session by revoking the refresh token the client currently
/// holds, so it can no longer be rotated into a fresh access token.
/// </summary>
/// <param name="RefreshToken">The opaque refresh token previously issued to the client. Never persisted.</param>
public record LogoutCommand(
    string RefreshToken
) : IRequest<Unit>;

/// <summary>
/// Handles the execution of the <see cref="LogoutCommand"/>. It looks the presented token up by its
/// irreversible hash and revokes it.
///
/// The operation is deliberately idempotent and always reports success: an unknown token and an
/// already-revoked token are handled exactly like a token that was just revoked, so the endpoint
/// cannot be used to probe which token values exist (no token enumeration).
/// </summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly ILogger<LogoutCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogoutCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="refreshTokenGenerator">The service that hashes the presented refresh token for lookup.</param>
    /// <param name="logger">The structured logger used for security-relevant audit events.</param>
    public LogoutCommandHandler(
        IApplicationDbContext context,
        IRefreshTokenGenerator refreshTokenGenerator,
        ILogger<LogoutCommandHandler> logger)
    {
        _context = context;
        _refreshTokenGenerator = refreshTokenGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Revokes the presented refresh token asynchronously.
    /// </summary>
    /// <param name="request">The command carrying the refresh token submitted by the client.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>A completed <see cref="Unit"/> value; the endpoint always reports success.</returns>
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Only the hash is ever stored, so the presented value is hashed for the lookup. The raw token
        // is never compared or persisted, and is never written to the log.
        var presentedHash = _refreshTokenGenerator.ComputeHash(request.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

        if (storedToken is null || storedToken.IsRevoked)
        {
            // Idempotent by design: signing out of an unknown or already-ended session is not an error,
            // and answering identically in both cases means the response reveals nothing about which
            // token values exist.
            _logger.LogInformation("Logout completed; the presented token was unknown or already revoked.");

            return Unit.Value;
        }

        storedToken.Revoke(DateTime.UtcNow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request (typically a rotation) revoked the token first. That is exactly the
            // end state logout wants, so the lost race is treated as success rather than surfaced.
            _logger.LogInformation("Logout raced with a concurrent request; the token is already revoked.");

            return Unit.Value;
        }

        // Audit-relevant security event (recorded at Information). Only the opaque user identifier is
        // recorded; no token value is ever logged.
        _logger.LogInformation("Logout succeeded. UserId={UserId}", storedToken.UserId);

        return Unit.Value;
    }
}
