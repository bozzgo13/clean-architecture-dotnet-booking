using MediatR;
using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Bookings.Queries.GetBookings;

/// <summary>
/// Represents a CQRS Query that retrieves bookings visible to the caller.
/// Implements MediatR's IRequest to return a read-only list of <see cref="BookingDto"/> projections.
/// </summary>
/// <param name="RequestingUserId">The unique identifier of the user issuing the request (extracted from the current user context/claims).</param>
/// <param name="IsAdministrator">
/// When <c>true</c> the caller is an administrator and every booking in the system is returned;
/// otherwise only bookings owned by <paramref name="RequestingUserId"/> are returned.
/// </param>
public record GetBookingsQuery(
    Guid RequestingUserId,
    bool IsAdministrator
) : IRequest<List<BookingDto>>;

/// <summary>
/// Handles the execution of the <see cref="GetBookingsQuery"/>.
/// Reads bookings directly from the persistence abstraction and applies role-based filtering.
/// </summary>
public class GetBookingsQueryHandler : IRequestHandler<GetBookingsQuery, List<BookingDto>>
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetBookingsQueryHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    public GetBookingsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves bookings asynchronously, projected into <see cref="BookingDto"/> instances.
    /// </summary>
    /// <param name="request">The query describing the requesting user and their role.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>A list of bookings the caller is allowed to see.</returns>
    public async Task<List<BookingDto>> Handle(GetBookingsQuery request, CancellationToken cancellationToken)
    {
        // Read-only query: AsNoTracking avoids the change-tracker overhead for data we never modify.
        IQueryable<Booking> query = _context.Bookings.AsNoTracking();

        // Business rule: regular users may only see their own bookings, administrators see everything.
        if (!request.IsAdministrator)
        {
            query = query.Where(b => b.UserId == request.RequestingUserId);
        }

        // Project the entities directly into the DTO within the database query (no entity materialization).
        return await query
            .OrderBy(b => b.StartTime)
            .Select(b => new BookingDto(
                b.Id,
                b.UserId,
                b.ResourceId,
                b.StartTime,
                b.EndTime,
                b.Status))
            .ToListAsync(cancellationToken);
    }
}
