using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Auth.Commands.Register;

/// <summary>
/// Represents a CQRS Command that registers a new user account.
/// Implements MediatR's IRequest to return the unique identifier (Guid) of the created user.
/// </summary>
/// <param name="Email">The email address of the new account. Stored normalized (trimmed and lowercased).</param>
/// <param name="Password">The plaintext password. Never persisted as-is; hashed before storage.</param>
/// <param name="FirstName">The user's first name.</param>
/// <param name="LastName">The user's last name.</param>
public record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName
) : IRequest<Guid>;

/// <summary>
/// Handles the execution of the <see cref="RegisterCommand"/>.
/// Enforces the email uniqueness business rule, hashes the password, and persists a new <see cref="User"/>.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RegisterCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    /// <param name="passwordHasher">The password hashing service used to secure the credential.</param>
    /// <param name="logger">The structured logger used for security-relevant audit events.</param>
    public RegisterCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<RegisterCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <summary>
    /// Processes the registration command asynchronously.
    /// </summary>
    /// <param name="request">The command containing the new account details.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>The unique identifier (Guid) of the newly created user.</returns>
    /// <exception cref="ValidationException">
    /// Thrown when an account with the same email address already exists, regardless of casing.
    /// </exception>
    public async Task<Guid> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Normalize up front so the uniqueness check matches the value that will be persisted.
        var normalizedEmail = User.NormalizeEmail(request.Email);

        // Business rule: an email address may only be registered once, regardless of casing.
        var emailAlreadyExists = await _context.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailAlreadyExists)
        {
            // Audit-relevant security event: a registration was rejected because the email is already
            // registered. Only the (normalized) email is recorded; the password is never logged.
            _logger.LogWarning(
                "User registration rejected because the email is already registered. Email={Email}",
                normalizedEmail);

            // Surfaced as a validation failure so the global handler maps it to 400 Bad Request.
            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    nameof(RegisterCommand.Email),
                    "An account with this email address already exists.")
            });
        }

        // Hash the credential before it ever reaches the domain entity or the database.
        var passwordHash = _passwordHasher.HashPassword(request.Password);

        // Instantiate the User entity using Rich Domain Model principles: the constructor
        // normalizes the email and enforces the core invariants.
        var user = new User(normalizedEmail, passwordHash, request.FirstName, request.LastName);

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEmailConstraintViolation(exception))
        {
            // The application-level check above and this INSERT are not atomic: two concurrent
            // registrations of the same email can both pass the pre-check and then race to the
            // database, where the unique index on Users.Email rejects the loser with a DbUpdateException.
            // Translating that specific failure into the same ValidationException keeps the API contract
            // stable (400 Bad Request) instead of leaking a 500 Internal Server Error.
            // Genuine, unrelated database failures are deliberately NOT caught here and still surface as 500s.
            _logger.LogWarning(
                "User registration rejected by the unique email constraint during a concurrent request. Email={Email}",
                normalizedEmail);

            throw new ValidationException(new[]
            {
                new ValidationFailure(
                    nameof(RegisterCommand.Email),
                    "An account with this email address already exists.")
            });
        }

        // Audit-relevant security event: a new account was created. Only the opaque identifier is
        // logged, never the password or the plaintext credential.
        _logger.LogInformation("User registration succeeded. UserId={UserId}", user.Id);

        return user.Id;
    }

    /// <summary>
    /// Determines whether a <see cref="DbUpdateException"/> was caused by a uniqueness conflict
    /// (a duplicate value hitting a unique index), rather than some other database failure.
    /// </summary>
    /// <param name="exception">The exception raised by Entity Framework Core.</param>
    /// <returns><c>true</c> when the underlying error indicates a unique-constraint violation.</returns>
    private static bool IsUniqueEmailConstraintViolation(DbUpdateException exception)
    {
        // Provider-agnostic check: SQL Server reports duplicate-key (2601) and unique-constraint (2627)
        // violations using messages that contain "duplicate"/"unique", and other relational providers
        // do the same. Email is the only unique index on Users, so matching these keywords is sufficient
        // to recognize the registration race without binding the Application layer to a specific provider.
        var innerException = exception.InnerException;
        if (innerException is null)
        {
            return false;
        }

        return innerException.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || innerException.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}
