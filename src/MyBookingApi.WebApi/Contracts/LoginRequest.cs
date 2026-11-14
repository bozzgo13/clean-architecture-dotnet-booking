namespace MyBookingApi.WebApi.Contracts;

/// <summary>
/// Request contract for authenticating a user (<c>POST /api/v1/auth/login</c>).
/// It is an explicit DTO separate from the domain model and from the Application command, so a client
/// can only ever bind the two fields it is allowed to supply and never influence entity state directly.
/// </summary>
/// <param name="Email">The account email address.</param>
/// <param name="Password">The plaintext password to verify.</param>
public sealed record LoginRequest(
    string Email,
    string Password);
