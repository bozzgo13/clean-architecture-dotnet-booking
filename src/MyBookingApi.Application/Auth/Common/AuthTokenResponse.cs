namespace MyBookingApi.Application.Auth.Common;

/// <summary>
/// The credential pair handed back to a client after a successful authentication or token refresh.
/// It deliberately carries no personal data and no password material (data minimization).
/// </summary>
/// <param name="AccessToken">The signed JWT access token used to authenticate subsequent requests.</param>
/// <param name="ExpiresAtUtc">The instant (UTC) at which the access token expires.</param>
/// <param name="RefreshToken">
/// The opaque, cryptographically secure value the client presents to obtain a new access token without
/// re-sending the password.
/// </param>
public sealed record AuthTokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken);
