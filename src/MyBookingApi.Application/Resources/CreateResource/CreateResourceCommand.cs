using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Resources.Commands.CreateResource;

/// <summary>
/// Represents a CQRS Command that creates a new bookable resource.
/// Implements MediatR's IRequest to return the <see cref="ResourceDto"/> of the successfully created resource.
/// </summary>
/// <param name="Name">The unique display name of the resource.</param>
/// <param name="Description">An optional free-text description of the resource.</param>
/// <param name="IsActive">Whether the resource is created as available for booking. Defaults to <c>true</c>.</param>
public record CreateResourceCommand(
    string Name,
    string? Description,
    bool IsActive = true
) : IRequest<ResourceDto>;

/// <summary>
/// Handles the execution of the <see cref="CreateResourceCommand"/>.
/// Enforces the resource-name uniqueness business rule, creates the domain entity and persists it.
/// </summary>
public class CreateResourceCommandHandler : IRequestHandler<CreateResourceCommand, ResourceDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CreateResourceCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateResourceCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="logger">The structured logger used for security-relevant audit events.</param>
    public CreateResourceCommandHandler(
        IApplicationDbContext context,
        ILogger<CreateResourceCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Processes the create resource command asynchronously.
    /// </summary>
    /// <param name="request">The command containing the resource details.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The <see cref="ResourceDto"/> describing the newly persisted resource.</returns>
    /// <exception cref="ValidationException">
    /// Thrown when a resource with the same name already exists.
    /// </exception>
    public async Task<ResourceDto> Handle(CreateResourceCommand request, CancellationToken cancellationToken)
    {
        // Business rule: a resource name may only be used once.
        var nameAlreadyExists = await _context.Resources
            .AnyAsync(r => r.Name == request.Name, cancellationToken);

        if (nameAlreadyExists)
        {
            // Audit-relevant security event: an administrative action was rejected. Only the resource
            // name is recorded; it is not a secret but is enough to correlate the attempt.
            _logger.LogWarning(
                "Resource creation rejected because a resource with the same name already exists. Name={Name}",
                request.Name);

            // Surfaced as a validation failure so the global handler maps it to 400 Bad Request.
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    nameof(CreateResourceCommand.Name),
                    "A resource with this name already exists.")
            });
        }

        // Instantiate the Resource entity using Rich Domain Model principles. The description is
        // normalized to an empty string so a missing optional value never violates the non-nullable
        // domain invariant, and the availability is applied through the entity's own behavior method.
        var resource = new Resource(request.Name, request.Description ?? string.Empty);
        resource.SetAvailability(request.IsActive);

        _context.Resources.Add(resource);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueNameConstraintViolation(exception))
        {
            // The application-level check above and this INSERT are not atomic: two concurrent
            // administrative requests for the same name can both pass the pre-check and then race to
            // the database, where the unique index on Resources.Name rejects the loser with a
            // DbUpdateException. Translating that into the same ValidationException keeps the API
            // contract stable (400 Bad Request) instead of leaking a 500 Internal Server Error.
            // Genuine, unrelated database failures are deliberately NOT caught here and still surface as 500s.
            _logger.LogWarning(
                "Resource creation rejected by the unique name constraint during a concurrent request. Name={Name}",
                request.Name);

            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    nameof(CreateResourceCommand.Name),
                    "A resource with this name already exists.")
            });
        }

        // Structured audit logging (Section 4): record the privileged state change. Only the opaque
        // identifier is logged.
        _logger.LogInformation("Resource created. ResourceId={ResourceId}", resource.Id);

        return new ResourceDto(resource.Id, resource.Name, resource.Description, resource.IsAvailable);
    }

    /// <summary>
    /// Determines whether a <see cref="DbUpdateException"/> was caused by a uniqueness conflict
    /// (a duplicate value hitting a unique index), rather than some other database failure.
    /// </summary>
    /// <param name="exception">The exception raised by Entity Framework Core.</param>
    /// <returns><c>true</c> when the underlying error indicates a unique-constraint violation.</returns>
    private static bool IsUniqueNameConstraintViolation(DbUpdateException exception)
    {
        // Provider-agnostic check: SQL Server reports duplicate-key (2601) and unique-constraint (2627)
        // violations using messages that contain "duplicate"/"unique", and other relational providers
        // do the same. Name is the only unique index on Resources, so matching these keywords is
        // sufficient to recognize the race without binding the Application layer to a specific provider.
        var innerException = exception.InnerException;
        if (innerException is null)
        {
            return false;
        }

        return innerException.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || innerException.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}
