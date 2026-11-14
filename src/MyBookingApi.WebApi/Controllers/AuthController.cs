using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Auth.Commands.Register;

namespace MyBookingApi.WebApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _mediator;

    public AuthController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Registers a new user account and returns the newly created user's identifier.
    /// </summary>
    /// <param name="command">The registration details (email, password, first and last name).</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The unique identifier of the newly created user.</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Guid>> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        // The controller stays thin: it only forwards the command through MediatR to the handler.
        var userId = await _mediator.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, userId);
    }
}
