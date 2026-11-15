using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBookingApi.Application.Auth.Commands.Login;
using MyBookingApi.Application.Auth.Commands.Logout;
using MyBookingApi.Application.Auth.Commands.Refresh;
using MyBookingApi.Application.Auth.Commands.Register;
using MyBookingApi.Application.Auth.Common;
using MyBookingApi.WebApi.Contracts;

namespace MyBookingApi.WebApi.Controllers;

/// <summary>
/// Exposes the authentication endpoints under the versioned <c>/api/v1/auth</c> route.
///
/// The controller is deny-by-default: the class-level <see cref="AuthorizeAttribute"/> requires an
/// authenticated caller for any action that does not opt out. The four endpoints that legitimately
/// serve unauthenticated callers opt out explicitly with <see cref="AllowAnonymousAttribute"/>, so a
/// newly added action cannot become public by accident (OWASP API5:2023 - BFLA).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Authorize]
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
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthTokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        // Map the transport contract onto the Application command; the controller never sees the domain.
        var command = new LoginUserCommand(request.Email, request.Password);

        var tokens = await _mediator.Send(command, cancellationToken);

        return Ok(tokens);
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new access token and a rotated refresh token. The
    /// presented token is revoked as part of the rotation and can never be redeemed again.
    /// </summary>
    /// <param name="request">The submitted refresh token.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The newly issued access and refresh tokens.</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthTokenResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        // Map the transport contract onto the Application command; the controller stays thin.
        var command = new RefreshTokenCommand(request.RefreshToken);

        var tokens = await _mediator.Send(command, cancellationToken);

        return Ok(tokens);
    }

    /// <summary>
    /// Ends the caller's session by revoking the supplied refresh token, so it can no longer be
    /// exchanged for a new access token. The operation is idempotent: an unknown or already-revoked
    /// token is treated the same as a valid one and the endpoint always reports success, so it cannot
    /// be used to probe which token values exist.
    /// </summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        // Map the transport contract onto the Application command; the controller stays thin.
        var command = new LogoutCommand(request.RefreshToken);

        await _mediator.Send(command, cancellationToken);

        // The response is deliberately empty: a body could only be used to disclose whether the
        // presented token existed, which the endpoint must never reveal.
        return NoContent();
    }
}
