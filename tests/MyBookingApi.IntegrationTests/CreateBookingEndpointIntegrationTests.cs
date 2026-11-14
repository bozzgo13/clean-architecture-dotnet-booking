using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Domain.Entities;
using MyBookingApi.Domain.Enums;
using MyBookingApi.Infrastructure.Persistence;
using MyBookingApi.WebApi.Contracts;
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
    /// must be persisted in the database, owned by the authenticated caller.
    /// </summary>
    [Fact]
    public async Task Create_AsAuthenticatedUser_ReturnsCreatedAndPersistsBooking()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var resource = new Resource("ISBN:9780747532743", "Harry Potter and the Philosopher's Stone");

        _factory.Seed(context => context.Resources.Add(resource));

        var payload = new CreateBookingRequest(
            resource.Id,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddHours(2));

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
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
    /// Mass-assignment / BOLA protection: a client that injects an extra <c>userId</c> field into the
    /// payload (attempting to create a booking on behalf of somebody else) must not be able to change
    /// the owner. The persisted booking must belong to the authenticated caller.
    /// </summary>
    [Fact]
    public async Task Create_WithClientSuppliedUserId_IgnoresOverpostedOwner()
    {
        // Arrange
        var authenticatedUserId = Guid.NewGuid();
        var impersonatedUserId = Guid.NewGuid();
        var resource = new Resource("ISBN:9780747532744", "Harry Potter and the Chamber of Secrets");

        _factory.Seed(context => context.Resources.Add(resource));

        // The DTO deliberately has no UserId property, so this field is ignored during deserialization.
        var overpostedPayload = new
        {
            UserId = impersonatedUserId,
            ResourceId = resource.Id,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(overpostedPayload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, authenticatedUserId.ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookingId = await response.Content.ReadFromJsonAsync<Guid>();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedBooking = await context.Bookings.SingleAsync(b => b.Id == bookingId);

        storedBooking.UserId.Should().Be(authenticatedUserId);
        storedBooking.UserId.Should().NotBe(impersonatedUserId);
    }

    /// <summary>
    /// Mass-assignment (API3): a client that injects privileged/state fields ("status": "Confirmed",
    /// "role": "Admin", "isAdmin": true) into the booking payload must not influence server-side
    /// state. The created booking keeps the server-assigned default status.
    /// </summary>
    [Fact]
    public async Task Create_WithOverpostedStateFields_IgnoresThem()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var resource = new Resource("ISBN:9780747532745", "Harry Potter and the Prisoner of Azkaban");

        _factory.Seed(context => context.Resources.Add(resource));

        var overpostedPayload = new
        {
            ResourceId = resource.Id,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            status = "Confirmed",
            role = "Admin",
            isAdmin = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(overpostedPayload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookingId = await response.Content.ReadFromJsonAsync<Guid>();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedBooking = await context.Bookings.SingleAsync(b => b.Id == bookingId);

        storedBooking.Status.Should().Be(BookingStatus.Pending);
        storedBooking.UserId.Should().Be(userId);
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task Create_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new CreateBookingRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddHours(2));

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

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
        var payload = new CreateBookingRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(-1).AddHours(2));

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Information-disclosure protection: an unexpected server error must be returned as a generic
    /// ProblemDetails payload that does not leak exception types, internal messages or stack traces.
    /// </summary>
    [Fact]
    public async Task Create_WhenUnexpectedErrorOccurs_ReturnsGenericProblemDetailsWithoutInternals()
    {
        // Arrange: booking a non-existent resource makes the handler throw an unexpected exception.
        var payload = new CreateBookingRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddHours(2));

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("KeyNotFoundException");
        body.Should().NotContain("Resource with ID");
        body.Should().NotContain("MyBookingApi");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
