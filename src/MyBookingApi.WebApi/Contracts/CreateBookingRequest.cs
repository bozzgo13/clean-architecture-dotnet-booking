namespace MyBookingApi.WebApi.Contracts;

/// <summary>
/// Request contract for creating a booking (<c>POST /api/Bookings</c>).
/// Deliberately excludes the booking owner: the owner is derived from the authenticated caller's
/// identity, never from client input, which prevents overposting / broken object level
/// authorization (a client trying to create bookings on behalf of another user).
/// </summary>
/// <param name="ResourceId">The unique identifier of the resource to book.</param>
/// <param name="StartTime">The scheduled start time of the booking.</param>
/// <param name="EndTime">The scheduled end time of the booking.</param>
public sealed record CreateBookingRequest(
    Guid ResourceId,
    DateTime StartTime,
    DateTime EndTime);
