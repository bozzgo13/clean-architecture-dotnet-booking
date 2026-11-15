using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyBookingApi.Application.Common.Interfaces;

namespace MyBookingApi.Infrastructure.Security;

/// <summary>
/// Generates refresh tokens from the operating system's cryptographically secure random number
/// generator (CSPRNG) and hashes them with SHA-256 before they are persisted.
///
/// A plain SHA-256 digest (rather than a password KDF) is appropriate here because the token is 512
/// bits of uniform random data: it cannot be brute-forced or guessed, so a slow hash would only add
/// cost without adding security. Hashing at all means a database disclosure yields no usable token.
/// </summary>
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    /// <summary>Number of random bytes per token: 64 bytes (512 bits) of CSPRNG output.</summary>
    private const int TokenSizeInBytes = 64;

    private readonly JwtOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenGenerator"/> class.
    /// </summary>
    /// <param name="options">The validated token configuration, providing the refresh token lifetime.</param>
    public RefreshTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public RefreshTokenMaterial Generate()
    {
        // Hex encoding keeps the value safe to carry in JSON and in a URL without escaping.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));
        var expiresAtUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenLifetimeDays);

        return new RefreshTokenMaterial(token, ComputeHash(token), expiresAtUtc);
    }

    /// <inheritdoc />
    public string ComputeHash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
