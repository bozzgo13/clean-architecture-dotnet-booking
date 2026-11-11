using MediatR;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Bookings.Commands.CreateBooking;

/// <summary>
/// Represents a CQRS Command to create a new booking in the system.
/// Implements MediatR's IRequest to return the unique identifier (Guid) of the successfully created booking.
/// </summary>
/// <param name="UserId">The unique identifier of the user making the booking.</param>
/// <param name="ResourceId">The unique identifier of the resource being booked.</param>
/// <param name="StartTime">The scheduled start time of the booking.</param>
/// <param name="EndTime">The scheduled end time of the booking.</param>
public record CreateBookingCommand(
    Guid UserId,
    Guid ResourceId,
    DateTime StartTime,
    DateTime EndTime
) : IRequest<Guid>;

/// <summary>
/// Handles the execution of the <see cref="CreateBookingCommand"/>.
/// Responsible for coordinating resource validation, domain entity creation, and persistence.
/// </summary>
public class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateBookingCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    public CreateBookingCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Processes the create booking command asynchronously.
    /// </summary>
    /// <param name="request">The command containing booking details.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The unique identifier (Guid) of the newly persisted booking.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the specified resource does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the resource is unavailable for booking.</exception>
    public async Task<Guid> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify if the target resource exists in the persistence layer
        var resource = await _context.Resources
            .FindAsync(new object[] { request.ResourceId }, cancellationToken);

        if (resource == null)
            throw new KeyNotFoundException($"Resource with ID {request.ResourceId} was not found.");

        // 2. Validate current operational availability of the resource
        if (!resource.IsAvailable)
            throw new InvalidOperationException($"The requested resource with ID {request.ResourceId} is currently not available.");

        // 3. Instantiate the Booking entity using Rich Domain Model principles.
        // Core invariants (e.g., StartTime < EndTime) are strictly enforced inside the entity constructor.
        var booking = new Booking(
            request.UserId,
            request.ResourceId,
            request.StartTime,
            request.EndTime
        );

        // 4. Stage the new booking entity within the Entity Framework change tracker
        _context.Bookings.Add(booking);

        // 5. Persist changes to the underlying database via the Unit of Work pattern
        await _context.SaveChangesAsync(cancellationToken);

        // 6. Return the newly generated unique identifier of the booking
        return booking.Id;
    }
}