using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Domain.Enums;
using MyBookingApi.WebApi.Contracts;

namespace MyBookingApi.WebApi.Controllers;

/// <summary>
/// Exposes resource management endpoints under the versioned <c>/api/v1/resources</c> route.
///
/// The controller is deny-by-default: the class-level <see cref="AuthorizeAttribute"/> restricts every
/// action to the <see cref="UserRole.Admin"/> role, so unauthenticated callers are rejected with
/// 401 Unauthorized and authenticated non-administrators with 403 Forbidden before any handler runs
/// (OWASP API1:2023 - BOLA and API5:2023 - BFLA).
/// </summary>
[ApiController]
[Route("api/v1/resources")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class ResourcesController : ControllerBase
{
    private readonly ISender _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourcesController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR sender used to dispatch commands to the Application layer.</param>
    public ResourcesController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a new bookable resource. Restricted to administrators.
    /// </summary>
    /// <param name="request">The resource details (name, optional description and initial availability).</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>The created resource together with a Location header pointing at its canonical URI.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ResourceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ResourceDto>> Create(
        [FromBody] CreateResourceRequest request,
        CancellationToken cancellationToken)
    {
        // The controller stays thin: it maps the transport contract onto the Application command and
        // delegates. No domain or persistence concern is referenced here.
        var command = new CreateResourceCommand(request.Name, request.Description, request.IsActive);

        var resource = await _mediator.Send(command, cancellationToken);

        // Announce the canonical URI of the newly created resource (RFC 9110) alongside the 201 body.
        return Created($"/api/v1/resources/{resource.Id}", resource);
    }
}
