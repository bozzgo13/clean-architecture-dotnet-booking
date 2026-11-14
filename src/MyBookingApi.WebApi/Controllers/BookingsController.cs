using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;
using MyBookingApi.Application.Bookings.Queries.GetBookings;
using MyBookingApi.WebApi.Extensions;

namespace MyBookingApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly ISender _mediator;

    public BookingsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateBookingCommand command, CancellationToken cancellationToken)
    {
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
    [Authorize]
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
}
