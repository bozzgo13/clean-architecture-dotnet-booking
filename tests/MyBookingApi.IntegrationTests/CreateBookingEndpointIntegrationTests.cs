using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;
using MyBookingApi.Domain.Entities;
using MyBookingApi.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/Bookings</c>, exercising the full request
/// pipeline (authentication -> authorization -> controller -> MediatR -> validation -> EF Core).
/// </summary>
public class CreateBookingEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/Bookings";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CreateBookingEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// An authenticated user posting a valid payload must receive 201 Created and the booking
    /// must be persisted in the database.
    /// </summary>
    [Fact]
    public async Task Create_AsAuthenticatedUser_ReturnsCreatedAndPersistsBooking()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var resource = new Resource("ISBN:9780747532743", "Harry Potter and the Philosopher's Stone");

        _factory.Seed(context => context.Resources.Add(resource));

        var command = new CreateBookingCommand(
            UserId: userId,
            ResourceId: resource.Id,
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2));

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookingId = await response.Content.ReadFromJsonAsync<Guid>();
        bookingId.Should().NotBeEmpty();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedBooking = await context.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);

        storedBooking.Should().NotBeNull();
        storedBooking!.UserId.Should().Be(userId);
        storedBooking.ResourceId.Should().Be(resource.Id);
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task Create_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var command = new CreateBookingCommand(
            UserId: Guid.NewGuid(),
            ResourceId: Guid.NewGuid(),
            StartTime: DateTime.UtcNow.AddDays(1),
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2));

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An authenticated request with an invalid payload must fail validation and be mapped
    /// to 400 Bad Request by the global exception handler (ValidationBehavior).
    /// </summary>
    [Fact]
    public async Task Create_WithInvalidPayload_ReturnsBadRequest()
    {
        // Arrange: StartTime is in the past, which fails the CreateBookingCommandValidator rules.
        var command = new CreateBookingCommand(
            UserId: Guid.NewGuid(),
            ResourceId: Guid.NewGuid(),
            StartTime: DateTime.UtcNow.AddDays(-1),
            EndTime: DateTime.UtcNow.AddDays(-1).AddHours(2));

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
