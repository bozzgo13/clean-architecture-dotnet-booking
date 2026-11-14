using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Infrastructure.Security;

/// <summary>
/// Issues signed JWT access tokens with HMAC-SHA256 using the validated <see cref="JwtOptions"/>.
/// Only the issuer, audience, lifetime and signing key configured for the API are used, so a token
/// produced here is accepted by the JWT bearer authentication configured in the Web API.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    // Reusable, thread-safe handler; it holds no per-request state.
    private readonly JsonWebTokenHandler _tokenHandler = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenGenerator"/> class.
    /// </summary>
    /// <param name="options">The validated JWT configuration (issuer, audience, key and lifetime).</param>
    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public AccessToken GenerateAccessToken(User user)
    {
        var issuedAtUtc = DateTime.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAtUtc,
            NotBefore = issuedAtUtc,
            Expires = expiresAtUtc,
            SigningCredentials = signingCredentials,
            // Data minimization: only the claims the API actually authorizes against are embedded -
            // the user identifier (read by ClaimsPrincipalExtensions.GetUserId) and the role (read by
            // IsInRole). No other personal data is added to the token.
            Claims = new Dictionary<string, object>
            {
                [ClaimTypes.NameIdentifier] = user.Id.ToString(),
                [ClaimTypes.Role] = user.Role.ToString(),
                // A unique token identifier, useful for correlation and future revocation lists.
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            }
        };

        var token = _tokenHandler.CreateToken(descriptor);

        return new AccessToken(token, expiresAtUtc);
    }
}
