using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.Refresh;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="RefreshTokenCommandHandler"/> covering the refresh-token lifecycle:
/// successful rotation into a brand-new token pair, generic rejection of unknown/expired/revoked
/// tokens, and the defensive invalidation of every active token when a revoked token is replayed.
/// </summary>
public class RefreshTokenCommandHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly FakeJwtTokenGenerator _tokenGenerator;
    private readonly FakeRefreshTokenGenerator _refreshTokenGenerator;
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on LINQ-to-Entities (SingleOrDefaultAsync/Where), which cannot be faithfully
        // reproduced with a mock.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _tokenGenerator = new FakeJwtTokenGenerator();
        _refreshTokenGenerator = new FakeRefreshTokenGenerator();
        _handler = new RefreshTokenCommandHandler(
            _context,
            _tokenGenerator,
            _refreshTokenGenerator,
            NullLogger<RefreshTokenCommandHandler>.Instance);
    }

    /// <summary>
    /// A valid, active token must be rotated: a new access token and a new refresh token are issued,
    /// the presented token is revoked and linked to its successor, and the successor is persisted as active.
    /// </summary>
    [Fact]
    public async Task Handle_ActiveToken_ShouldRotateAndIssueNewTokenPair()
    {
        // Arrange
        var user = await SeedUserAsync();
        var storedToken = await SeedRefreshTokenAsync(user.Id, "presented-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var command = new RefreshTokenCommand("presented-raw-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: a fresh pair was returned.
        result.AccessToken.Should().Be($"token-for::{user.Id}");
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        result.RefreshToken.Should().NotBe("presented-raw-token");
        _tokenGenerator.RequestedFor.Should().Be(user.Id);

        // Assert: the presented token was revoked and records which token replaced it.
        storedToken.IsRevoked.Should().BeTrue();
        storedToken.ReplacedByTokenHash.Should().Be(_refreshTokenGenerator.ComputeHash(result.RefreshToken));

        // Assert: exactly one active token remains, and it is the newly issued one.
        var tokens = await _context.RefreshTokens.ToListAsync();
        tokens.Should().HaveCount(2);
        var active = tokens.Single(t => t.IsActive);
        active.TokenHash.Should().Be(_refreshTokenGenerator.ComputeHash(result.RefreshToken));
        active.UserId.Should().Be(user.Id);
    }

    /// <summary>
    /// A token that cannot be matched to a stored hash must be rejected with the generic
    /// <see cref="UnauthorizedAccessException"/> and must not issue any token.
    /// </summary>
    [Fact]
    public async Task Handle_UnknownToken_ShouldThrowUnauthorized()
    {
        // Arrange
        await SeedUserAsync();

        var command = new RefreshTokenCommand("never-issued-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenGenerator.RequestedFor.Should().BeNull();
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// An expired token must be rejected with the generic <see cref="UnauthorizedAccessException"/>
    /// and must not be rotated.
    /// </summary>
    [Fact]
    public async Task Handle_ExpiredToken_ShouldThrowUnauthorizedAndNotRotate()
    {
        // Arrange
        var user = await SeedUserAsync();
        var expired = await SeedRefreshTokenAsync(user.Id, "expired-raw-token", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));

        var command = new RefreshTokenCommand("expired-raw-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenGenerator.RequestedFor.Should().BeNull();
        expired.IsRevoked.Should().BeFalse();
        (await _context.RefreshTokens.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Presenting an already-revoked token is treated as a possible token-theft attempt: the request is
    /// rejected AND every other active token for the same user is revoked as a precaution.
    /// </summary>
    [Fact]
    public async Task Handle_RevokedToken_ShouldThrowUnauthorizedAndRevokeAllActiveTokens()
    {
        // Arrange: one revoked token (the replay) and one still-active token for the same user.
        var user = await SeedUserAsync();

        var replayed = await SeedRefreshTokenAsync(user.Id, "revoked-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        replayed.Revoke(DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var stillActive = await SeedRefreshTokenAsync(user.Id, "active-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var command = new RefreshTokenCommand("revoked-raw-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var tokens = await _context.RefreshTokens.ToListAsync();
        tokens.Should().OnlyContain(t => t.IsRevoked);
        stillActive.IsRevoked.Should().BeTrue();
        tokens.Should().NotContain(t => t.IsActive);
    }

    /// <summary>
    /// A token whose owning account no longer exists must be rejected rather than minting a token for
    /// an unknown identity.
    /// </summary>
    [Fact]
    public async Task Handle_TokenWithoutExistingUser_ShouldThrowUnauthorized()
    {
        // Arrange: a token is stored for a user that was never created.
        await SeedRefreshTokenAsync(Guid.NewGuid(), "orphan-raw-token", DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var command = new RefreshTokenCommand("orphan-raw-token");

        // Act
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenGenerator.RequestedFor.Should().BeNull();
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
    /// reproducible hash so the tests can assert on rotation without the real CSPRNG.
    /// </summary>
    private class FakeRefreshTokenGenerator : IRefreshTokenGenerator
    {
        private int _counter;

        public RefreshTokenMaterial Generate()
        {
            var token = $"rotated-token::{++_counter}";

            return new RefreshTokenMaterial(token, ComputeHash(token), DateTime.UtcNow.AddDays(7));
        }

        public string ComputeHash(string token) => $"hash::{token}";
    }
}
