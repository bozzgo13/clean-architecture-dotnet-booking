namespace MyBookingApi.Application.Resources.Commands.CreateResource;

/// <summary>
/// Read-model returned after a resource is created. Returning this dedicated DTO instead of the
/// <see cref="Domain.Entities.Resource"/> entity keeps the domain model encapsulated and guarantees
/// that only allow-listed properties are ever serialized (OWASP API3:2023 - Broken Object Property
/// Level Authorization / Excessive Data Exposure).
/// </summary>
/// <param name="Id">The unique identifier of the created resource.</param>
/// <param name="Name">The resource name.</param>
/// <param name="Description">The resource description.</param>
/// <param name="IsAvailable">Whether the resource is currently available for booking.</param>
public record ResourceDto(
    Guid Id,
    string Name,
    string Description,
    bool IsAvailable);
