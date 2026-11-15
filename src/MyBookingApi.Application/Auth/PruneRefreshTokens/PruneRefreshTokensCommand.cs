using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Common.Interfaces;

namespace MyBookingApi.Application.Auth.Commands.PruneRefreshTokens;

/// <summary>
/// Represents a maintenance command that deletes refresh tokens which are useless and only occupy
/// space: tokens whose lifetime elapsed before the supplied cutoff, and tokens that were revoked
/// before it. It bounds the growth of the token store so it cannot be used to exhaust resources
/// (OWASP API4:2023 - Unrestricted Resource Consumption).
/// </summary>
/// <param name="CutoffUtc">
/// The instant (UTC) before which a token must have expired or been revoked to be eligible for
/// deletion. Everything newer is retained so the rotation/audit trail survives for a grace period.
/// </param>
public record PruneRefreshTokensCommand(
    DateTime CutoffUtc
) : IRequest<int>;

/// <summary>
/// Handles the execution of the <see cref="PruneRefreshTokensCommand"/> by deleting every refresh
/// token that expired or was revoked before the command's cutoff, and reporting how many rows were
/// removed.
/// </summary>
public class PruneRefreshTokensCommandHandler : IRequestHandler<PruneRefreshTokensCommand, int>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PruneRefreshTokensCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PruneRefreshTokensCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="logger">The structured logger used to report the outcome of a cleanup run.</param>
    public PruneRefreshTokensCommandHandler(
        IApplicationDbContext context,
        ILogger<PruneRefreshTokensCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the stale refresh tokens asynchronously.
    /// </summary>
    /// <param name="request">The command carrying the cutoff instant.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The number of rows that were deleted.</returns>
    public async Task<int> Handle(PruneRefreshTokensCommand request, CancellationToken cancellationToken)
    {
        // A token is stale when it can no longer be redeemed and enough time has passed for the audit
        // trail to have served its purpose: either its lifetime ended before the cutoff, or it was
        // revoked before it. Revocation is final, so neither case can ever become active again.
        var staleTokens = await _context.RefreshTokens
            .Where(t => t.ExpiresAtUtc < request.CutoffUtc
                || (t.RevokedAtUtc != null && t.RevokedAtUtc < request.CutoffUtc))
            .ToListAsync(cancellationToken);

        if (staleTokens.Count == 0)
        {
            return 0;
        }

        _context.RefreshTokens.RemoveRange(staleTokens);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request modified one of the rows between the read and the delete, so the
            // whole batch was rolled back. Nothing is lost: those rows are simply picked up by the
            // next scheduled run.
            _logger.LogWarning("Refresh-token cleanup was skipped because a row was modified concurrently.");

            return 0;
        }

        _logger.LogInformation("Refresh-token cleanup removed {RemovedCount} stale token(s).", staleTokens.Count);

        return staleTokens.Count;
    }
}
