using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Auth.Common;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Auth.Commands.Login;

/// <summary>
/// Represents a CQRS Command that authenticates a user with an email and password and, on success,
/// issues a signed JWT access token together with a cryptographically secure refresh token.
/// </summary>
/// <param name="Email">The email address of the account. Matched case-insensitively (normalized).</param>
/// <param name="Password">The plaintext password to verify. Never persisted and never logged.</param>
public record LoginUserCommand(
    string Email,
    string Password
) : IRequest<AuthTokenResponse>;

/// <summary>
/// Handles the execution of the <see cref="LoginUserCommand"/>. It resolves the account by its
/// normalized email, verifies the supplied password against the stored hash in constant time with
/// respect to account existence, and - on success - issues and persists the access and refresh tokens.
/// </summary>
public class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, AuthTokenResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly ILogger<LoginUserCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginUserCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="passwordHasher">The password hashing service used to verify the credential.</param>
    /// <param name="jwtTokenGenerator">The service that issues the signed JWT access token.</param>
    /// <param name="refreshTokenGenerator">The service that generates and hashes the refresh token.</param>
    /// <param name="logger">The structured logger used for security-relevant audit events.</param>
    public LoginUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenGenerator refreshTokenGenerator,
        ILogger<LoginUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenGenerator = refreshTokenGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates the supplied credentials asynchronously.
    /// </summary>
    /// <param name="request">The command carrying the submitted email and password.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The issued access and refresh tokens when the credentials are valid.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the email is unknown or the password does not match. The same generic exception is
    /// raised in both cases so the response cannot be used to enumerate valid accounts; the global
    /// exception handler maps it to a generic 401 Unauthorized.
    /// </exception>
    public async Task<AuthTokenResponse> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        // Look the account up by its normalized email so the comparison is case-insensitive and
        // matches the value persisted during registration. The entity is only read, never mutated,
        // so it is fetched without change tracking.
        var normalizedEmail = User.NormalizeEmail(request.Email);

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Always run a verification, even when no account matched: IPasswordHasher.VerifyPassword
        // performs a dummy verification for a null hash, so the time taken to reject a login does not
        // reveal whether the email address is registered (account-enumeration / timing-attack defense).
        var passwordIsValid = _passwordHasher.VerifyPassword(user?.PasswordHash, request.Password);

        if (user is null || !passwordIsValid)
        {
            // Audit-relevant security event (recorded at Warning). The message is deliberately generic
            // and identical for both failure modes, and neither the password nor its hash is ever
            // written to the log.
            _logger.LogWarning("Login failed for Email={Email}.", normalizedEmail);

            // Surfaced as UnauthorizedAccessException so the global handler maps it to a generic
            // 401 Unauthorized that does not distinguish "unknown account" from "wrong password".
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user);

        // Persist the refresh token (as a hash) so it can be looked up, rotated and revoked later.
        var now = DateTime.UtcNow;
        var refreshToken = _refreshTokenGenerator.Generate();
        _context.RefreshTokens.Add(
            new RefreshToken(user.Id, refreshToken.TokenHash, now, refreshToken.ExpiresAtUtc));

        await _context.SaveChangesAsync(cancellationToken);

        // Audit-relevant security event (recorded at Information): a successful authentication. Only
        // the opaque user identifier is recorded; no credential or token value is logged.
        _logger.LogInformation("Login succeeded. UserId={UserId}", user.Id);

        return new AuthTokenResponse(accessToken.Token, accessToken.ExpiresAtUtc, refreshToken.Token);
    }
}
