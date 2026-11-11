using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;
using MyBookingApi.Domain.Entities;
using MyBookingApi.Infrastructure.Persistence;
using Xunit;

public class CreateBookingCommandIntegrationTests
{
    private readonly DbContextOptions<AppDbContext> _dbContextOptions;

    public CreateBookingCommandIntegrationTests()
    {
        // Configure a unique In-Memory database for each test run to ensure complete isolation
        _dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    /// <summary>
    /// Integration test verifying that the CreateBookingCommandHandler successfully 
    /// validates the resource, creates a booking, and persists it into the database 
    /// using a real Entity Framework Core context (InMemory).
    /// </summary>
    public async Task Handle_ValidCommand_ShouldPersistBookingInDatabase()
    {
        // Arrange
        using var context = new AppDbContext(_dbContextOptions);

        var userId = Guid.NewGuid();

        // Seed a valid resource into the database so the handler can find it
        var resource = new Resource("ISBN:9780747532743", "Harry Potter and the Philosopher's Stone");
        context.Resources.Add(resource);
        await context.SaveChangesAsync();

        // Create the command using the actual ID of the seeded resource
        var command = new CreateBookingCommand(
            ResourceId: resource.Id, // Use the generated ID from the database entity
            UserId: userId,
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2)
        );

        var handler = new CreateBookingCommandHandler(context);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();

        // Verify that data was actually written to the database context
        using var verificationContext = new AppDbContext(_dbContextOptions);
        var storedBooking = await verificationContext.Bookings.FirstOrDefaultAsync(b => b.Id == result);

        storedBooking.Should().NotBeNull();
        storedBooking!.UserId.Should().Be(userId);
    }

    [Fact]
    /// <summary>
    /// Integration test verifying that trying to book a non-existent resource 
    /// throws an exception and leaves the bookings table empty.
    /// </summary>
    public async Task Handle_NonExistentResource_ShouldThrowExceptionAndNotPersist()
    {
        // Arrange
        using var context = new AppDbContext(_dbContextOptions);

        var command = new CreateBookingCommand(
            ResourceId: Guid.NewGuid(), // Resource does not exist in the database
            UserId: Guid.NewGuid(),
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2)
        );

        var handler = new CreateBookingCommandHandler(context);

        // Act & Assert
        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();

        // Verify that no booking was persisted
        using var verificationContext = new AppDbContext(_dbContextOptions);
        var bookingCount = await verificationContext.Bookings.CountAsync();
        bookingCount.Should().Be(0);
    }
}