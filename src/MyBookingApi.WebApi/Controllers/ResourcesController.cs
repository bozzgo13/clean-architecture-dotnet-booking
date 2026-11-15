using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBookingApi.Application.Common.Models;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Application.Resources.Queries.GetResources;
using MyBookingApi.Domain.Enums;
using MyBookingApi.WebApi.Contracts;

namespace MyBookingApi.WebApi.Controllers;

/// <summary>
/// Exposes resource management endpoints under the versioned <c>/api/v1/resources</c> route.
///
/// The controller is deny-by-default: the class-level <see cref="AuthorizeAttribute"/> requires an
/// authenticated caller for every action, so anonymous requests are rejected with 401 Unauthorized
/// before any handler runs. Actions that mutate state additionally demand the
/// <see cref="UserRole.Admin"/> role at the action level, so authenticated non-administrators receive
/// 403 Forbidden (OWASP API1:2023 - BOLA and API5:2023 - BFLA).
/// </summary>
[ApiController]
[Route("api/v1/resources")]
[Authorize]
public class ResourcesController : ControllerBase
{
    private readonly ISender _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourcesController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR sender used to dispatch requests to the Application layer.</param>
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
    [Authorize(Roles = nameof(UserRole.Admin))]
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

    /// <summary>
    /// Retrieves a paginated, optionally filtered list of resources.
    /// Available to any authenticated caller; only the allow-listed read-model fields are returned.
    /// </summary>
    /// <param name="pageNumber">The 1-based page to return. Defaults to 1.</param>
    /// <param name="pageSize">The number of items per page. Defaults to 10 and is capped by the validator.</param>
    /// <param name="searchTerm">An optional substring matched against the resource name.</param>
    /// <param name="isAvailable">An optional availability filter (true/false); omitted returns both.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>A paginated envelope of resource read models with paging metadata.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ResourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResponse<ResourceDto>>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = GetResourcesQueryValidator.DefaultPageSize,
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isAvailable = null,
        CancellationToken cancellationToken = default)
    {
        // The controller only maps the query-string parameters onto the Application query; pagination
        // bounds and filter lengths are enforced by the validator inside the MediatR pipeline.
        var query = new GetResourcesQuery(pageNumber, pageSize, searchTerm, isAvailable);

        var resources = await _mediator.Send(query, cancellationToken);

        return Ok(resources);
    }
}
