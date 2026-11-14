using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;
using MyBookingApi.Application.Bookings.Queries.GetBookings;
using MyBookingApi.Domain.Enums;
using MyBookingApi.WebApi.Contracts;
using MyBookingApi.WebApi.Extensions;

namespace MyBookingApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly ISender _mediator;

    public BookingsController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a booking owned by the authenticated caller.
    /// </summary>
    /// <param name="request">The booking details (resource and time window) supplied by the client.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The unique identifier of the newly created booking.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        // The booking owner is NEVER taken from the request body to prevent overposting / IDOR 
        // (OWASP API1:2023 BOLA & API3:2023 Mass Assignment): derived from the authenticated identity instead.
        var userId = User.GetUserId();
        var command = new CreateBookingCommand(userId, request.ResourceId, request.StartTime, request.EndTime);

        // Send the command through MediatR to the corresponding handler in the Application layer.
        var bookingId = await _mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = bookingId }, bookingId);
    }

    /// <summary>
    /// Retrieves the bookings visible to the authenticated caller.
    /// Regular users receive only their own bookings, whereas administrators receive all bookings.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The list of bookings the caller is authorized to see.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BookingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<BookingDto>>> GetBookings(CancellationToken cancellationToken)
    {
        // The controller stays thin: it only resolves the caller from the authenticated principal
        // and delegates the read operation to MediatR. Extracting the values into locals keeps them
        // easy to inspect at breakpoints while debugging.
        var userId = User.GetUserId();
        var isAdmin = User.IsAdmin();

        var query = new GetBookingsQuery(userId, isAdmin);

        var bookings = await _mediator.Send(query, cancellationToken);

        return Ok(bookings);
    }

    /// <summary>
    /// Administrative view that returns every booking in the system.
    /// Unlike <see cref="GetBookings"/>, access is restricted to the <see cref="UserRole.Admin"/> role at the
    /// endpoint level via <see cref="AuthorizeAttribute"/>, so a regular authenticated user is rejected with
    /// 403 Forbidden before the action runs (OWASP API5:2023 - Broken Function Level Authorization).
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>Every booking in the system.</returns>
    [HttpGet("all")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(IReadOnlyList<BookingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BookingDto>>> GetAllBookings(CancellationToken cancellationToken)
    {
        // The endpoint is role-gated by [Authorize(Roles = "Admin")]; the identity is still derived from the
        // authenticated principal (never from client input) to preserve the object-level authorization contract.
        var userId = User.GetUserId();

        var query = new GetBookingsQuery(userId, IsAdministrator: true);

        var bookings = await _mediator.Send(query, cancellationToken);

        return Ok(bookings);
    }
}
