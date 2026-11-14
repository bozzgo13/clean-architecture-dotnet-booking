using MyBookingApi.Domain.Enums;

namespace MyBookingApi.Application.Bookings.Queries.GetBookings;

/// <summary>
/// Read-model used to project a <see cref="Domain.Entities.Booking"/> into a dedicated,
/// transport-friendly shape. Returning this DTO (instead of the domain entity) keeps the
/// domain model encapsulated and prevents persistence concerns from leaking to the API surface.
/// </summary>
/// <param name="Id">The unique identifier of the booking.</param>
/// <param name="UserId">The unique identifier of the user who owns the booking.</param>
/// <param name="ResourceId">The unique identifier of the booked resource.</param>
/// <param name="StartTime">The scheduled start time of the booking.</param>
/// <param name="EndTime">The scheduled end time of the booking.</param>
/// <param name="Status">The current lifecycle status of the booking.</param>
public record BookingDto(
    Guid Id,
    Guid UserId,
    Guid ResourceId,
    DateTime StartTime,
    DateTime EndTime,
    BookingStatus Status
);
