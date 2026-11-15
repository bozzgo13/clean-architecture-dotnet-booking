using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.Register;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

public class RegisterCommandHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly FakePasswordHasher _passwordHasher;
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on AnyAsync()/LINQ-to-Entities, which cannot be faithfully reproduced with Moq.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _passwordHasher = new FakePasswordHasher();
        _handler = new RegisterCommandHandler(_context, _passwordHasher, NullLogger<RegisterCommandHandler>.Instance);
    }

    /// <summary>
    /// Tests that a valid regeistration creates a user, normalizes the email and
    /// stores the hashed (nevr the plaintext) password.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateUser()
    {
        // Arrange: deliberately mixed casing and surrounding whitespace in the email.
        var command = new RegisterCommand("  Janez.Novak@emAil.COM  ", "M0cn0Ge$10!", "Janez", "Novak");

        // Act
        var userId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        userId.Should().NotBeEmpty();

        var storedUser = await _context.Users.SingleAsync();
        storedUser.Id.Should().Be(userId);
        storedUser.Email.Should().Be("janez.novak@email.com");
        storedUser.FirstName.Should().Be("Janez");
        storedUser.LastName.Should().Be("Novak");
        storedUser.PasswordHash.Should().NotBe(command.Password);
        storedUser.PasswordHash.Should().Be(_passwordHasher.HashPassword(command.Password));
    }

    /// <summary>
    /// Tests that registering with an email that already exists (differing only by casing)
    /// is rejected and no additional user is persisted.
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateEmail_ShouldThrowAndNotPersist()
    {
        // Arrange: seed an existing account.
        _context.Users.Add(new User("user@email.com", "N2nQ4!s@Jc", "Existing", "User"));
        await _context.SaveChangesAsync();

        var command = new RegisterCommand("User@email.com", "vh438T5M3*uOmw", "New", "User");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        (await _context.Users.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Tests that a unique-constraint violation raised by the database during persistence
    /// (a concurrent registration race that slipped past the application-level check) is
    /// translated into the same validation failure used for duplicate emails.
    /// </summary>
    [Fact]
    public async Task Handle_UniqueConstraintViolationOnSave_ShouldThrowValidationException()
    {
        // Arrange: the database rejects the insert because the email index is already taken.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var innerException = new Exception(
            "Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Users_Email'.");
        using var context = new ThrowingSaveChangesDbContext(options, new DbUpdateException("Save failed.", innerException));
        var handler = new RegisterCommandHandler(context, _passwordHasher, NullLogger<RegisterCommandHandler>.Instance);

        var command = new RegisterCommand("manarkz@skyemails.tech", "UnoMana007!", "Mana", "RKZ");

        // Act
        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterCommand.Email));
    }

    /// <summary>
    /// Tests that a database update failure unrelated to uniqueness still propagates,
    /// so genuine infrastructure problems continue to be reported as server errors.
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
        var handler = new RegisterCommandHandler(context, _passwordHasher, NullLogger<RegisterCommandHandler>.Instance);

        var command = new RegisterCommand("realemmastone@itis.me", "Not4Long!", "Emma", "Stone");

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
