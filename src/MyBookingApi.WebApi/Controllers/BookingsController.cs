using MediatR;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;

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
    public async Task<ActionResult<Guid>> Create([FromBody] CreateBookingCommand command, CancellationToken cancellationToken)
    {
        // MediatR posreduje komando ustreznemu Handlerju, ki smo ga definirali v Application sloju
        var bookingId = await _mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = bookingId }, bookingId);
    }
}