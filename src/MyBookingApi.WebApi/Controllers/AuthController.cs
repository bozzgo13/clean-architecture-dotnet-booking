using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBookingApi.Application.Auth.Commands.Login;
using MyBookingApi.Application.Auth.Commands.Register;
using MyBookingApi.WebApi.Contracts;

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
    [EnableRateLimiting("registration")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Guid>> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        // The controller stays thin: it only forwards the command through MediatR to the handler.
        var userId = await _mediator.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, userId);
    }

    /// <summary>
    /// Authenticates a user against their stored credentials and issues an access token and a
    /// refresh token on success.
    /// </summary>
    /// <param name="request">The submitted credentials (email and password).</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The issued access and refresh tokens.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(LoginUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginUserResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        // Map the transport contract onto the Application command; the controller never sees the domain.
        var command = new LoginUserCommand(request.Email, request.Password);

        var tokens = await _mediator.Send(command, cancellationToken);

        return Ok(tokens);
    }
}
