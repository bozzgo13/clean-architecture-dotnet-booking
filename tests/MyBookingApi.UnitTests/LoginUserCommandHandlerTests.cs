using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.Login;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="LoginUserCommandHandler"/> covering the authentication business rules:
/// successful issuance of tokens, generic rejection of invalid credentials, case-insensitive account
/// resolution and the timing-equalizing password check.
/// </summary>
public class LoginUserCommandHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly FakePasswordHasher _passwordHasher;
    private readonly FakeJwtTokenGenerator _tokenGenerator;
    private readonly FakeRefreshTokenGenerator _refreshTokenGenerator;
    private readonly LoginUserCommandHandler _handler;

    public LoginUserCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on LINQ-to-Entities (SingleOrDefaultAsync/AsNoTracking), which cannot be faithfully
        // reproduced with a mock.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _passwordHasher = new FakePasswordHasher();
        _tokenGenerator = new FakeJwtTokenGenerator();
        _refreshTokenGenerator = new FakeRefreshTokenGenerator();
        _handler = new LoginUserCommandHandler(
            _context,
            _passwordHasher,
            _tokenGenerator,
            _refreshTokenGenerator,
            NullLogger<LoginUserCommandHandler>.Instance);
    }

    /// <summary>
    /// A valid email/password pair must yield an access token, its expiry and a non-empty refresh token.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCredentials_ShouldReturnAccessAndRefreshTokens()
    {
        // Arrange
        var user = new User("user@email.com", _passwordHasher.HashPassword("Str0ng!Pass"), "Janez", "Novak");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var command = new LoginUserCommand("user@email.com", "Str0ng!Pass");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.AccessToken.Should().Be($"token-for::{user.Id}");
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();

        // The refresh token was persisted (as a hash) so it can later be rotated and revoked.
        var storedToken = await _context.RefreshTokens.SingleAsync();
        storedToken.UserId.Should().Be(user.Id);
        storedToken.TokenHash.Should().Be(_refreshTokenGenerator.ComputeHash(result.RefreshToken));
        storedToken.IsActive.Should().BeTrue();

        // The token was minted for the account that was resolved from the database.
        _tokenGenerator.RequestedFor.Should().Be(user.Id);
    }

    /// <summary>
    /// The account lookup must be case-insensitive and ignore surrounding whitespace, matching the
    /// normalization applied at registration time.
    /// </summary>
    [Fact]
    public async Task Handle_EmailWithDifferentCasing_ShouldAuthenticate()
    {
        // Arrange
        _context.Users.Add(new User("janez.novak@email.com", _passwordHasher.HashPassword("M0cn0Ge$10!"), "Janez", "Novak"));
        await _context.SaveChangesAsync();

        var command = new LoginUserCommand("  Janez.Novak@EMAIL.com  ", "M0cn0Ge$10!");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A password that does not match the stored hash must be rejected with a generic
    /// <see cref="UnauthorizedAccessException"/> and must not issue a token.
    /// </summary>
    [Fact]
    public async Task Handle_WrongPassword_ShouldThrowUnauthorizedAndNotIssueToken()
    {
        // Arrange
        _context.Users.Add(new User("user@email.com", _passwordHasher.HashPassword("Str0ng!Pass"), "Janez", "Novak"));
        await _context.SaveChangesAsync();

        var command = new LoginUserCommand("user@email.com", "Wr0ng!Pass");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenGenerator.RequestedFor.Should().BeNull();
    }

    /// <summary>
    /// An unknown email must be rejected with the same generic <see cref="UnauthorizedAccessException"/>
    /// as a wrong password, so the response cannot be used to enumerate accounts.
    /// </summary>
    [Fact]
    public async Task Handle_UnknownEmail_ShouldThrowUnauthorized()
    {
        // Arrange: no user is seeded for this address.
        var command = new LoginUserCommand("ghost@email.com", "Str0ng!Pass");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenGenerator.RequestedFor.Should().BeNull();
    }

    /// <summary>
    /// When no account matches, the handler must still run a password verification against a null hash
    /// so the rejection consumes a comparable amount of work and does not leak account existence
    /// through response timing (account-enumeration / timing-attack defense).
    /// </summary>
    [Fact]
    public async Task Handle_UnknownEmail_ShouldStillPerformPasswordVerification()
    {
        // Arrange
        var command = new LoginUserCommand("ghost@email.com", "Str0ng!Pass");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        // Assert: the hasher was invoked exactly once, with a null stored hash.
        _passwordHasher.VerifyCallCount.Should().Be(1);
        _passwordHasher.LastVerifiedHash.Should().BeNull();
    }

    /// <summary>
    /// Both failing credential paths must produce the same exception message so a client cannot
    /// distinguish "unknown account" from "invalid password".
    /// </summary>
    [Fact]
    public async Task Handle_WrongPasswordAndUnknownEmail_ShouldProduceSameMessage()
    {
        // Arrange
        _context.Users.Add(new User("user@email.com", _passwordHasher.HashPassword("Str0ng!Pass"), "Janez", "Novak"));
        await _context.SaveChangesAsync();

        // Act
        var wrongPassword = await Record.ExceptionAsync(
            () => _handler.Handle(new LoginUserCommand("user@email.com", "Wr0ng!Pass"), CancellationToken.None));
        var unknownEmail = await Record.ExceptionAsync(
            () => _handler.Handle(new LoginUserCommand("ghost@email.com", "Str0ng!Pass"), CancellationToken.None));

        // Assert
        wrongPassword.Should().BeOfType<UnauthorizedAccessException>();
        unknownEmail.Should().BeOfType<UnauthorizedAccessException>();
        unknownEmail!.Message.Should().Be(wrongPassword!.Message);
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
    /// Deterministic password hasher that also records how the verification was invoked, so the tests
    /// can assert the timing-equalizing behavior without depending on the real hashing algorithm.
    /// </summary>
    private class FakePasswordHasher : IPasswordHasher
    {
        public int VerifyCallCount { get; private set; }

        public string? LastVerifiedHash { get; private set; }

        public string HashPassword(string password) => $"hashed::{password}";

        public bool VerifyPassword(string? hashedPassword, string providedPassword)
        {
            VerifyCallCount++;
            LastVerifiedHash = hashedPassword;

            return hashedPassword is not null && hashedPassword == HashPassword(providedPassword);
        }
    }

    /// <summary>
    /// Deterministic token generator that records the user it was asked to mint a token for.
    /// </summary>
    private class FakeJwtTokenGenerator : IJwtTokenGenerator
    {
        public Guid? RequestedFor { get; private set; }

        public AccessToken GenerateAccessToken(User user)
        {
            RequestedFor = user.Id;

            return new AccessToken($"token-for::{user.Id}", DateTime.UtcNow.AddMinutes(15));
        }
    }

    /// <summary>
    /// Deterministic refresh-token generator: each call yields a unique token with a stable,
    /// reproducible hash so the tests can assert on persistence and rotation without the real CSPRNG.
    /// </summary>
    private class FakeRefreshTokenGenerator : IRefreshTokenGenerator
    {
        private int _counter;

        public RefreshTokenMaterial Generate()
        {
            var token = $"refresh-token::{++_counter}";

            return new RefreshTokenMaterial(token, ComputeHash(token), DateTime.UtcNow.AddDays(7));
        }

        public string ComputeHash(string token) => $"hash::{token}";
    }
}
