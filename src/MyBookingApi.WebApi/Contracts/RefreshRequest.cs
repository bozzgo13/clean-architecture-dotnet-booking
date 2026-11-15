namespace MyBookingApi.WebApi.Contracts;

/// <summary>
/// Request contract for exchanging a refresh token for a new token pair
/// (<c>POST /api/v1/auth/refresh</c>). It is an explicit DTO separate from the Application command, so
/// a client can only ever bind the single field it is allowed to supply.
/// </summary>
/// <param name="RefreshToken">The opaque refresh token previously issued to the client.</param>
public sealed record RefreshRequest(
    string RefreshToken);
