namespace MyBookingApi.WebApi.HostedServices;

/// <summary>
/// Opt-in configuration for first-run administrator provisioning.
///
/// It intentionally carries no password: the one-time credential is generated in memory by the hosted
/// service and is never read from configuration or source control. The only value an operator supplies
/// is the (non-secret) administrator email, and only for the very first deployment.
/// </summary>
public sealed class AdminBootstrapOptions
{
    /// <summary>The configuration section name this options type binds to.</summary>
    public const string SectionName = "AdminBootstrap";

    /// <summary>
    /// When false (the default) the bootstrap path is completely inert. Set it to true only for the
    /// first deployment and remove it once the administrator exists.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The administrator's email address. Required only when <see cref="Enabled"/> is true.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>The administrator's first name. Defaults to a neutral placeholder.</summary>
    public string FirstName { get; set; } = "Initial";

    /// <summary>The administrator's last name. Defaults to a neutral placeholder.</summary>
    public string LastName { get; set; } = "Administrator";
}
