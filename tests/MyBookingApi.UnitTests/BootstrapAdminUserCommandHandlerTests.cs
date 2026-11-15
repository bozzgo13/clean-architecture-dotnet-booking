using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.Bootstrap;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using MyBookingApi.Domain.Enums;
using Xunit;

/// <summary>
/// Unit tests for <see cref="BootstrapAdminUserCommandHandler"/>, verifying that the first-run
/// administrator provisioning is idempotent, never mints a second administrator, hashes the credential
/// and converges gracefully when a concurrent replica wins the insert race.
/// </summary>
public class BootstrapAdminUserCommandHandlerTests
{
    private const string BootstrapEmail = "initial.admin@email.com";
    private const string BootstrapPassword = "Abcdef1!ghij";

    private readonly TestApplicationDbContext _context;
    private readonly FakePasswordHasher _passwordHasher;
    private readonly BootstrapAdminUserCommandHandler _handler;

    public BootstrapAdminUserCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on AnyAsync()/LINQ-to-Entities, which cannot be faithfully reproduced with Moq.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _passwordHasher = new FakePasswordHasher();
        _handler = new BootstrapAdminUserCommandHandler(
            _context,
            _passwordHasher,
            NullLogger<BootstrapAdminUserCommandHandler>.Instance);
    }

    private static BootstrapAdminUserCommand ValidCommand() =>
        new(BootstrapEmail, BootstrapPassword, "Initial", "Administrator");

    /// <summary>
    /// Tests that bootstrapping against an empty database provisions exactly one administrator with
    /// the stored role set to <see cref="UserRole.Admin"/> and the credential hashed, never plaintext.
    /// </summary>
    [Fact]
    public async Task Handle_EmptyDatabase_ShouldProvisionAdministrator()
    {
        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.Created);

        var admin = await _context.Users.SingleAsync();
        admin.Email.Should().Be(BootstrapEmail);
        admin.FirstName.Should().Be("Initial");
        admin.LastName.Should().Be("Administrator");
        admin.Role.Should().Be(UserRole.Admin);
        admin.PasswordHash.Should().NotBe(BootstrapPassword);
        admin.PasswordHash.Should().Be(_passwordHasher.HashPassword(BootstrapPassword));
    }

    /// <summary>
    /// Tests that the email is normalized (trimmed and lowercased) before it is persisted, so the
    /// uniqueness check and the stored value agree regardless of the configured casing.
    /// </summary>
    [Fact]
    public async Task Handle_MixedCaseEmail_ShouldNormalizeBeforePersisting()
    {
        // Arrange: deliberately mixed casing and surrounding whitespace.
        var command = ValidCommand() with { Email = "  Initial.Admin@EMail.COM  " };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.Created);
        var admin = await _context.Users.SingleAsync();
        admin.Email.Should().Be(BootstrapEmail);
    }

    /// <summary>
    /// Tests that re-running the bootstrap for an account that already exists is a no-op reported as
    /// <see cref="AdminBootstrapResult.AlreadyExists"/>, which is what makes the command safe to invoke
    /// on every startup and from every replica.
    /// </summary>
    [Fact]
    public async Task Handle_ExistingAccount_ShouldReturnAlreadyExistsAndNotPersist()
    {
        // Arrange: seed an existing administrator with the same email (differing only by casing).
        _context.Users.Add(new User(BootstrapEmail, "existing-hash", "Existing", "Admin", UserRole.Admin));
        await _context.SaveChangesAsync();

        var command = ValidCommand() with { Email = "Initial.Admin@Email.com" };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.AlreadyExists);
        (await _context.Users.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Tests that a second administrator is never minted: when an administrator already exists under a
    /// different email, provisioning is refused and nothing is written.
    /// </summary>
    [Fact]
    public async Task Handle_AnotherAdministratorExists_ShouldRefuseProvisioning()
    {
        // Arrange
        _context.Users.Add(new User("other.admin@email.com", "existing-hash", "Other", "Admin", UserRole.Admin));
        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.SkippedBecauseAdminExists);
        (await _context.Users.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Tests that ordinary (non-administrator) accounts do not block the very first administrator from
    /// being provisioned.
    /// </summary>
    [Fact]
    public async Task Handle_OnlyRegularUsersExist_ShouldStillProvisionAdministrator()
    {
        // Arrange
        _context.Users.Add(new User("regular@email.com", "existing-hash", "Regular", "User"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.Created);
        (await _context.Users.CountAsync(u => u.Role == UserRole.Admin)).Should().Be(1);
    }

    /// <summary>
    /// Tests that a unique-constraint violation raised during persistence (a concurrent replica winning
    /// the insert race) is treated as convergence: the desired end state (exactly one administrator) was
    /// reached, so it is reported as <see cref="AdminBootstrapResult.AlreadyExists"/> rather than failing.
    /// </summary>
    [Fact]
    public async Task Handle_UniqueConstraintViolationOnSave_ShouldConvergeAsAlreadyExists()
    {
        // Arrange: the database rejects the insert because the email index is already taken.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var innerException = new Exception(
            "Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Users_Email'.");
        using var context = new ThrowingSaveChangesDbContext(options, new DbUpdateException("Save failed.", innerException));
        var handler = new BootstrapAdminUserCommandHandler(
            context,
            _passwordHasher,
            NullLogger<BootstrapAdminUserCommandHandler>.Instance);

        // Act
        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.Should().Be(AdminBootstrapResult.AlreadyExists);
    }

    /// <summary>
    /// Tests that a database failure unrelated to uniqueness still propagates, so genuine infrastructure
    /// problems are surfaced instead of being mistaken for a successful convergence.
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
        var handler = new BootstrapAdminUserCommandHandler(
            context,
            _passwordHasher,
            NullLogger<BootstrapAdminUserCommandHandler>.Instance);

        // Act
        Func<Task> act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

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

    /// <summary>
    /// Deterministic password hasher used to keep the handler test focused on orchestration
    /// rather than the hashing algorithm itself.
    /// </summary>
    private class FakePasswordHasher : IPasswordHasher
    {
        public string HashPassword(string password) => $"hashed::{password}";

        public bool VerifyPassword(string? hashedPassword, string providedPassword)
            => hashedPassword is not null && hashedPassword == HashPassword(providedPassword);
    }
}
