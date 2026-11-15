using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.Logout;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="LogoutCommandHandler"/> covering the sign-out lifecycle: revoking the
/// presented token, revoking only that token (never the whole account), and remaining idempotent so
/// an unknown or already-revoked token is treated as a successful no-op.
/// </summary>
public class LogoutCommandHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly FakeRefreshTokenGenerator _refreshTokenGenerator;
    private readonly LogoutCommandHandler _handler;

    public LogoutCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on LINQ-to-Entities (SingleOrDefaultAsync), which cannot be faithfully reproduced
        // with a mock.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _refreshTokenGenerator = new FakeRefreshTokenGenerator();
        _handler = new LogoutCommandHandler(
            _context,
            _refreshTokenGenerator,
            NullLogger<LogoutCommandHandler>.Instance);
    }

    /// <summary>
    /// The presented active token must be revoked, and only that token: logout ends a single session
    /// and must not sign the user out of their other devices.
    /// </summary>
    [Fact]
    public async Task Handle_ActiveToken_ShouldRevokeOnlyThePresentedToken()
    {
        // Arrange: the presented token plus a second active token for the same user.
        var user = await SeedUserAsync();
        var presented = await SeedRefreshTokenAsync(user.Id, "presented-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        var other = await SeedRefreshTokenAsync(user.Id, "other-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var command = new LogoutCommand("presented-raw-token");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert: only the presented token was revoked, and logout is not a rotation so no successor is recorded.
        presented.IsRevoked.Should().BeTrue();
        presented.ReplacedByTokenHash.Should().BeNull();
        other.IsRevoked.Should().BeFalse();
    }

    /// <summary>
    /// A token that was never issued must be accepted as a successful no-op, so the endpoint reveals
    /// nothing about which token values exist.
    /// </summary>
    [Fact]
    public async Task Handle_UnknownToken_ShouldSucceedIdempotently()
    {
        // Arrange
        await SeedUserAsync();

        var command = new LogoutCommand("never-issued-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Logging out twice must be safe: an already-revoked token is treated as a successful no-op
    /// rather than raising an error.
    /// </summary>
    [Fact]
    public async Task Handle_AlreadyRevokedToken_ShouldSucceedIdempotently()
    {
        // Arrange
        var user = await SeedUserAsync();
        var revoked = await SeedRefreshTokenAsync(user.Id, "revoked-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        revoked.Revoke(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var command = new LogoutCommand("revoked-raw-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        revoked.IsRevoked.Should().BeTrue();
    }

    /// <summary>
    /// The raw refresh token must never be persisted: only its hash is stored, so a database
    /// disclosure yields no usable credential.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldNeverPersistTheRawToken()
    {
        // Arrange
        var user = await SeedUserAsync();
        await SeedRefreshTokenAsync(user.Id, "presented-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var command = new LogoutCommand("presented-raw-token");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var storedHashes = await _context.RefreshTokens.Select(t => t.TokenHash).ToListAsync();
        storedHashes.Should().NotBeEmpty();
        storedHashes.Should().NotContain("presented-raw-token");
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User("user@email.com", "hashed::Str0ng!Pass", "Janez", "Novak");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }

    private async Task<RefreshToken> SeedRefreshTokenAsync(Guid userId, string rawToken, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        var token = new RefreshToken(userId, _refreshTokenGenerator.ComputeHash(rawToken), createdAtUtc, expiresAtUtc);
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        return token;
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
    /// Deterministic refresh-token generator: a stable, reproducible hash so the tests can assert on
    /// what was persisted without invoking the real CSPRNG.
    /// </summary>
    private class FakeRefreshTokenGenerator : IRefreshTokenGenerator
    {
        public RefreshTokenMaterial Generate()
        {
            var token = "generated-raw-token";

            return new RefreshTokenMaterial(token, ComputeHash(token), DateTime.UtcNow.AddDays(7));
        }

        public string ComputeHash(string token) => $"hash::{token}";
    }
}
