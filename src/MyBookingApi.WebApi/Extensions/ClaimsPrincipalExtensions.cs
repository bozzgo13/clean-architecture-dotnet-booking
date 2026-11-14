using System.Security.Claims;
using MyBookingApi.Domain.Enums;

namespace MyBookingApi.WebApi.Extensions;

/// <summary>
/// Extension methods that centralize extraction of the authenticated caller's identity from the
/// request's <see cref="ClaimsPrincipal"/>, keeping controller actions free of claims plumbing.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the authenticated user's unique identifier from the <see cref="ClaimTypes.NameIdentifier"/> claim.
    /// </summary>
    /// <param name="principal">The authenticated principal (typically <c>HttpContext.User</c>).</param>
    /// <returns>The authenticated user's identifier.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the identifier claim is missing or not a valid <see cref="Guid"/>.
    /// The global exception handler translates this into a 401 Unauthorized response.
    /// </exception>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("The authenticated user identifier is missing or invalid.");
        }

        return userId;
    }

    /// <summary>
    /// Determines whether the authenticated user holds the administrator role.
    /// </summary>
    /// <param name="principal">The authenticated principal (typically <c>HttpContext.User</c>).</param>
    /// <returns><c>true</c> when the user is an administrator; otherwise <c>false</c>.</returns>
    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        return principal.IsInRole(nameof(UserRole.Admin));
    }
}
