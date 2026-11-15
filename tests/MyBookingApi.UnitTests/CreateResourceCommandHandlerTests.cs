using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CreateResourceCommandHandler"/>, verifying successful creation, the
/// availability mapping, the resource-name uniqueness business rule and safe handling of database
/// concurrency failures.
/// </summary>
public class CreateResourceCommandHandlerTests
{
    private const string ResourceName = "ConfRoom-A:1F";
    private const string ResourceDescription = "First-floor conference room A with a projector";

    private readonly TestApplicationDbContext _context;
    private readonly CreateResourceCommandHandler _handler;

    public CreateResourceCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on AnyAsync()/LINQ-to-Entities, which cannot be faithfully reproduced with Moq.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _handler = new CreateResourceCommandHandler(_context, NullLogger<CreateResourceCommandHandler>.Instance);
    }

    /// <summary>
    /// Tests that a valid command creates the resource, persists it and returns a DTO describing it.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateResourceAndReturnDto()
    {
        // Arrange
        var command = new CreateResourceCommand(ResourceName, ResourceDescription);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be(ResourceName);
        result.Description.Should().Be(ResourceDescription);
        result.IsAvailable.Should().BeTrue();

        var storedResource = await _context.Resources.SingleAsync();
        storedResource.Id.Should().Be(result.Id);
        storedResource.Name.Should().Be(ResourceName);
        storedResource.Description.Should().Be(ResourceDescription);
    }

    /// <summary>
    /// Tests that a resource created as inactive is persisted as unavailable, applying the domain
    /// behavior method rather than setting the property directly.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIsActiveFalse_ShouldPersistResourceAsUnavailable()
    {
        // Arrange
        var command = new CreateResourceCommand(ResourceName, ResourceDescription, IsActive: false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsAvailable.Should().BeFalse();

        var storedResource = await _context.Resources.SingleAsync();
        storedResource.IsAvailable.Should().BeFalse();
    }

    /// <summary>
    /// Tests that an omitted (null) description is normalized to an empty string so the non-nullable
    /// domain invariant is preserved.
    /// </summary>
    [Fact]
    public async Task Handle_WithNullDescription_ShouldPersistEmptyDescription()
    {
        // Arrange
        var command = new CreateResourceCommand(ResourceName, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Description.Should().BeEmpty();

        var storedResource = await _context.Resources.SingleAsync();
        storedResource.Description.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that creating a resource whose name is already taken is rejected and nothing is persisted.
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateName_ShouldThrowAndNotPersist()
    {
        // Arrange: seed an existing resource with the same name.
        _context.Resources.Add(new Resource(ResourceName, "Existing resource"));
        await _context.SaveChangesAsync();

        var command = new CreateResourceCommand(ResourceName, ResourceDescription);

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        (await _context.Resources.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Tests that a unique-constraint violation raised by the database during persistence (a concurrent
    /// request that slipped past the application-level check) is translated into the same validation
    /// failure used for duplicate names, so the API contract stays a 400 rather than a 500.
    /// </summary>
    [Fact]
    public async Task Handle_UniqueConstraintViolationOnSave_ShouldThrowValidationException()
    {
        // Arrange: the database rejects the insert because the name index is already taken.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var innerException = new Exception(
            "Cannot insert duplicate key row in object 'dbo.Resources' with unique index 'IX_Resources_Name'.");
        using var context = new ThrowingSaveChangesDbContext(options, new DbUpdateException("Save failed.", innerException));
        var handler = new CreateResourceCommandHandler(context, NullLogger<CreateResourceCommandHandler>.Instance);

        var command = new CreateResourceCommand(ResourceName, ResourceDescription);

        // Act
        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateResourceCommand.Name));
    }

    /// <summary>
    /// Tests that a database failure unrelated to uniqueness still propagates, so genuine infrastructure
    /// problems continue to be reported as server errors.
    /// </summary>
    [Fact]
    public async Task Handle_UnrelatedDbUpdateExceptionOnSave_ShouldPropagate()
    {
        // Arrange: the inner error is a connectivity problem, not a uniqueness conflict.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var innerException = new Exception("A network-related error occurred while establishing a connection to the server.");
        using var context = new ThrowingSaveChangesDbContext(options, new DbUpdateException("Save failed.", innerException));
        var handler = new CreateResourceCommandHandler(context, NullLogger<CreateResourceCommandHandler>.Instance);

        var command = new CreateResourceCommand(ResourceName, ResourceDescription);

        // Act
        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Minimal EF Core context used to exercise the handler against a real query provider
    /// without depending on the Infrastructure layer.
    /// </summary>
    private class TestApplicationDbContext : DbContext, IApplicationDbContext
    {
        public TestApplicationDbContext(DbContextOptions<TestApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Resource> Resources => Set<Resource>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    }

    /// <summary>
    /// In-memory context variant that throws a configured <see cref="DbUpdateException"/> on
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>, simulating a database failure
    /// (e.g. a unique-index violation) at the point of persistence.
    /// </summary>
    private class ThrowingSaveChangesDbContext : TestApplicationDbContext
    {
        private readonly DbUpdateException _exception;

        public ThrowingSaveChangesDbContext(DbContextOptions<TestApplicationDbContext> options, DbUpdateException exception)
            : base(options)
        {
            _exception = exception;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw _exception;
    }
}
