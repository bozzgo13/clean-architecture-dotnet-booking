using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyBookingApi.Application.Auth.Commands.PruneRefreshTokens;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="PruneRefreshTokensCommandHandler"/>, verifying that only tokens which
/// expired or were revoked before the cutoff are deleted, that anything newer is retained so the
/// audit trail survives its grace period, and that the handler reports how many rows it removed.
/// </summary>
public class PruneRefreshTokensCommandHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly PruneRefreshTokensCommandHandler _handler;

    public PruneRefreshTokensCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _handler = new PruneRefreshTokensCommandHandler(_context, NullLogger<PruneRefreshTokensCommandHandler>.Instance);
    }

    /// <summary>
    /// Tokens that expired or were revoked before the cutoff must be deleted, while tokens that are
    /// still active or only recently expired/revoked must be retained.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldDeleteOnlyTokensExpiredOrRevokedBeforeTheCutoff()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var cutoffUtc = now.AddDays(-7);

        var expiredLongAgo = await SeedAsync(userId, now.AddDays(-30), now.AddDays(-20));
        var revokedBeforeCutoff = await SeedAsync(userId, now.AddDays(-30), now.AddDays(30));
        revokedBeforeCutoff.Revoke(now.AddDays(-20));
        var expiredRecently = await SeedAsync(userId, now.AddDays(-3), now.AddDays(-1));
        var stillActive = await SeedAsync(userId, now.AddDays(-1), now.AddDays(7));
        await _context.SaveChangesAsync();

        var command = new PruneRefreshTokensCommand(cutoffUtc);

        // Act
        var removed = await _handler.Handle(command, CancellationToken.None);

        // Assert
        removed.Should().Be(2);

        var remaining = await _context.RefreshTokens.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().Contain(t => t.Id == expiredRecently.Id);
        remaining.Should().Contain(t => t.Id == stillActive.Id);
    }

    /// <summary>
    /// When nothing is stale the handler must delete nothing and report zero.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNothingIsStale_ShouldDeleteNothing()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedAsync(userId, now.AddDays(-1), now.AddDays(7));
        await SeedAsync(userId, now.AddDays(-2), now.AddDays(5));
        await _context.SaveChangesAsync();

        var command = new PruneRefreshTokensCommand(now.AddDays(-7));

        // Act
        var removed = await _handler.Handle(command, CancellationToken.None);

        // Assert
        removed.Should().Be(0);
        (await _context.RefreshTokens.CountAsync()).Should().Be(2);
    }

    private async Task<RefreshToken> SeedAsync(Guid userId, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        var token = new RefreshToken(userId, $"hash::{Guid.NewGuid()}", createdAtUtc, expiresAtUtc);
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
}
