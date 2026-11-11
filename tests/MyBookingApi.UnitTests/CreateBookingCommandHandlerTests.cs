using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

public class CreateBookingCommandHandlerTests
{
    private readonly Mock<IApplicationDbContext> _dbContextMock;
    private readonly Mock<DbSet<Booking>> _bookingsDbSetMock;
    private readonly Mock<DbSet<Resource>> _resourcesDbSetMock;
    private readonly CreateBookingCommandHandler _handler;

    public CreateBookingCommandHandlerTests()
    {
        _dbContextMock = new Mock<IApplicationDbContext>();
        _bookingsDbSetMock = new Mock<DbSet<Booking>>();
        _resourcesDbSetMock = new Mock<DbSet<Resource>>();

        // Setup DbContext to return the mockedDbSets
        _dbContextMock.Setup(db => db.Bookings).Returns(_bookingsDbSetMock.Object);
        _dbContextMock.Setup(db => db.Resources).Returns(_resourcesDbSetMock.Object);

        _handler = new CreateBookingCommandHandler(_dbContextMock.Object);
    }

    /// <summary>
    /// Tests that a valid booking command successfully creates a booking 
    /// when the resource exists and saves changes to the database.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateBookingSuccessfully()
    {

        // Arrange
        var resourceId = Guid.NewGuid();
        var command = new CreateBookingCommand(
            ResourceId: resourceId,
            UserId: Guid.NewGuid(),
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2)
        );

        var resource = new Resource("ISBN:9788131002087", "Aladin And The Magic Lamp");

        // Simulate that the resource exists in the database
        _dbContextMock
            .Setup(db => db.Resources.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resource);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();

        _bookingsDbSetMock.Verify(d => d.Add(It.IsAny<Booking>()), Times.Once);
        _dbContextMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that the handler rejects the booking creation request 
    /// and throws an exception when the specified resource does not exist.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Handle_NonExistentResource_ShouldReject()
    {
        // Arrange
        var resourceId = Guid.NewGuid();
        var command = new CreateBookingCommand(
            ResourceId: resourceId,
            UserId: Guid.NewGuid(),
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(1)
        );

        // Simulate that the resource does NOT exist (returns null)
        _dbContextMock
            .Setup(db => db.Resources.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Resource?)null);

        // Act & Assert
        Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();

        _bookingsDbSetMock.Verify(d => d.Add(It.IsAny<Booking>()), Times.Never);
    }
}