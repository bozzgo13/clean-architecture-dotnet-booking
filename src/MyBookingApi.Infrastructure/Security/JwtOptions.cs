using System.ComponentModel.DataAnnotations;

namespace MyBookingApi.Infrastructure.Security;

/// <summary>
/// Strongly-typed representation of the "Jwt" configuration section. The values are validated at
/// application startup (see <c>Program.cs</c>) so that a missing issuer/audience or a weak signing
/// key fails fast, instead of the API silently starting with tokens that anyone could forge.
///
/// It lives with the JWT infrastructure because both the access-token generator and the Web API
/// startup validation consume it.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>The configuration section name this options type binds to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>The expected token issuer (<c>iss</c> claim).</summary>
    [Required(ErrorMessage = "Jwt:Issuer is required.")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The expected token audience (<c>aud</c> claim).</summary>
    [Required(ErrorMessage = "Jwt:Audience is required.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The HMAC-SHA256 signing key. It must be at least 32 bytes (256 bits) and is supplied through
    /// environment variables or ASP.NET Core User Secrets; it is never committed to source control.
    /// </summary>
    [Required(ErrorMessage = "Jwt:Key is required. Provide it via environment variables or User Secrets.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters (256 bits) for HS256.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The lifetime, in minutes, of an issued access token. A short lifetime limits the window in
    /// which a leaked token can be replayed. Defaults to 15 minutes.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Jwt:AccessTokenLifetimeMinutes must be between 1 and 1440 minutes.")]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}
