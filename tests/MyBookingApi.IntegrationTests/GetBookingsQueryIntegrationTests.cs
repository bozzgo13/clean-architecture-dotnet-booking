using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyBookingApi.Application.Bookings.Queries.GetBookings;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>GET /api/bookings</c>, exercising the full request
/// pipeline (authentication -> authorization -> controller -> MediatR handler -> EF Core).
/// </summary>
public class GetBookingsQueryIntegrationTests : IDisposable
{
    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GetBookingsQueryIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// An authenticated regular user must receive 200 OK and only their own bookings,
    /// even when bookings belonging to other users exist.
    /// </summary>
    [Fact]
    public async Task GetBookings_AsAuthenticatedRegularUser_ReturnsOnlyOwnBookings()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var ownBooking = new Booking(requestingUserId, resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
        var otherUserBooking = new Booking(otherUserId, resourceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1));

        _factory.Seed(context => context.Bookings.AddRange(ownBooking, otherUserBooking));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/bookings");
        request.Headers.Add(TestAuthHandler.UserIdHeader, requestingUserId.ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bookings = await response.Content.ReadFromJsonAsync<List<BookingDto>>();
        bookings.Should().NotBeNull();
        bookings!.Should().ContainSingle();
        bookings![0].Id.Should().Be(ownBooking.Id);
        bookings!.Should().OnlyContain(b => b.UserId == requestingUserId);
    }

    /// <summary>
    /// An authenticated administrator must receive 200 OK and every seeded booking in the system.
    /// </summary>
    [Fact]
    public async Task GetBookings_AsAdministrator_ReturnsAllBookings()
    {
        // Arrange
        var resourceId = Guid.NewGuid();

        var firstBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
        var secondBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1));

        _factory.Seed(context => context.Bookings.AddRange(firstBooking, secondBooking));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/bookings");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bookings = await response.Content.ReadFromJsonAsync<List<BookingDto>>();
        bookings.Should().HaveCount(2);
        bookings!.Select(b => b.Id).Should().BeEquivalentTo(new[] { firstBooking.Id, secondBooking.Id });
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task GetBookings_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/bookings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An authenticated request whose user identifier claim is not a valid Guid must also be rejected
    /// with 401 Unauthorized, mapped by the global exception handler (UnauthorizedAccessException).
    /// </summary>
    [Fact]
    public async Task GetBookings_WhenUserIdentifierClaimIsInvalid_ReturnsUnauthorized()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/bookings");
        request.Headers.Add(TestAuthHandler.UserIdHeader, "not-a-valid-guid");
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// BFLA (API5): an authenticated regular user must receive 403 Forbidden when calling the
    /// admin-only endpoint, proving privileged functionality is gated by role, not by obscurity.
    /// </summary>
    [Fact]
    public async Task GetAllBookings_AsRegularUser_ReturnsForbidden()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/bookings/all");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// BFLA (API5): an administrator is authorized for the admin-only endpoint and receives every
    /// booking in the system.
    /// </summary>
    [Fact]
    public async Task GetAllBookings_AsAdministrator_ReturnsAllBookings()
    {
        // Arrange
        var resourceId = Guid.NewGuid();

        var firstBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1));
        var secondBooking = new Booking(Guid.NewGuid(), resourceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1));

        _factory.Seed(context => context.Bookings.AddRange(firstBooking, secondBooking));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/bookings/all");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bookings = await response.Content.ReadFromJsonAsync<List<BookingDto>>();
        bookings.Should().HaveCount(2);
        bookings!.Select(b => b.Id).Should().BeEquivalentTo(new[] { firstBooking.Id, secondBooking.Id });
    }

    /// <summary>
    /// An unauthenticated request to the admin-only endpoint must be rejected with 401 Unauthorized
    /// before any role evaluation takes place.
    /// </summary>
    [Fact]
    public async Task GetAllBookings_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/bookings/all");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
