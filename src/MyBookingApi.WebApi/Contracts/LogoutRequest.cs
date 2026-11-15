namespace MyBookingApi.WebApi.Contracts;

/// <summary>
/// Request contract for ending a session by revoking a refresh token
/// (<c>POST /api/v1/auth/logout</c>). It is an explicit DTO separate from the Application command, so
/// a client can only ever bind the single field it is allowed to supply.
/// </summary>
/// <param name="RefreshToken">The opaque refresh token currently held by the client.</param>
public sealed record LogoutRequest(
    string RefreshToken);
