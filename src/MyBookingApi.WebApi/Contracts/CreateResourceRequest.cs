namespace MyBookingApi.WebApi.Contracts;

/// <summary>
/// Request contract for creating a resource (<c>POST /api/v1/resources</c>).
/// It is an explicit, immutable DTO separate from both the domain model and the Application command,
/// so a client can only ever bind the fields it is allowed to supply and can never influence entity
/// state directly (OWASP API3:2023 - Mass Assignment).
/// </summary>
/// <param name="Name">The unique display name of the resource.</param>
/// <param name="Description">An optional free-text description of the resource.</param>
/// <param name="IsActive">Whether the resource is created as available for booking. Defaults to <c>true</c>.</param>
public sealed record CreateResourceRequest(
    string Name,
    string? Description,
    bool IsActive = true);
