using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Bookings.Queries.GetBookings;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

public class GetBookingsQueryHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly GetBookingsQueryHandler _handler;

    public GetBookingsQueryHandlerTests()
    {
        // A real (in-memory) EF Core context is used here instead of a mocked DbSet because the handler
        // relies on AsNoTracking()/LINQ-to-Entities, which cannot be faithfully reproduced with Moq.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _handler = new GetBookingsQueryHandler(_context);
    }

    /// <summary>
    /// Tests that a regular user only receives bookings that belong to them,
    /// while bookings owned by other users are filtered out.
    /// </summary>
    [Fact]
    public async Task Handle_RegularUser_ShouldReturnOnlyOwnBookings()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var ownBooking = new Booking(requestingUserId, resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
        var otherUserBooking = new Booking(otherUserId, resourceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1));

        _context.Bookings.AddRange(ownBooking, otherUserBooking);
        await _context.SaveChangesAsync();

        var query = new GetBookingsQuery(requestingUserId, IsAdministrator: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be(ownBooking.Id);
        result[0].UserId.Should().Be(requestingUserId);
    }

    /// <summary>
    /// Tests that an administrator receives every booking in the system,
    /// regardless of which user owns each booking.
    /// </summary>
    [Fact]
    public async Task Handle_Administrator_ShouldReturnAllBookings()
    {
        // Arrange
        var resourceId = Guid.NewGuid();

        var firstBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
        var secondBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1));

        _context.Bookings.AddRange(firstBooking, secondBooking);
        await _context.SaveChangesAsync();

        var query = new GetBookingsQuery(Guid.NewGuid(), IsAdministrator: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(b => b.Id).Should().BeEquivalentTo(new[] { firstBooking.Id, secondBooking.Id });
    }

    /// <summary>
    /// Tests that a regular user with no matching bookings receives an empty list
    /// instead of an exception or somebody else's data.
    /// </summary>
    [Fact]
    public async Task Handle_NoMatchingBookings_ShouldReturnEmptyList()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Seed a booking owned by a different user so the filter has something to exclude.
        _context.Bookings.Add(new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));
        await _context.SaveChangesAsync();

        var query = new GetBookingsQuery(requestingUserId, IsAdministrator: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
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
    }
}
