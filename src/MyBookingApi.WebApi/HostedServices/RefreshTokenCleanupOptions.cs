using System.ComponentModel.DataAnnotations;

namespace MyBookingApi.WebApi.HostedServices;

/// <summary>
/// Strongly-typed representation of the "RefreshTokenCleanup" configuration section, controlling the
/// background maintenance job that prunes stale refresh tokens. Both values have safe defaults, so
/// the section may be omitted entirely from configuration.
/// </summary>
public sealed class RefreshTokenCleanupOptions
{
    /// <summary>The configuration section name this options type binds to.</summary>
    public const string SectionName = "RefreshTokenCleanup";

    /// <summary>
    /// How often the cleanup job runs, in hours. Defaults to 24.
    /// </summary>
    [Range(1, 168, ErrorMessage = "RefreshTokenCleanup:IntervalHours must be between 1 and 168 hours.")]
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// The grace period, in days, for which a token is retained after it expired or was revoked, so
    /// the rotation/audit trail is not discarded immediately. Defaults to 7.
    /// </summary>
    [Range(1, 365, ErrorMessage = "RefreshTokenCleanup:RetentionDays must be between 1 and 365 days.")]
    public int RetentionDays { get; set; } = 7;
}
