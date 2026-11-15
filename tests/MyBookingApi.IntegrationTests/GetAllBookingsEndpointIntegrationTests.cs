using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MyBookingApi.Application.Bookings.Queries.GetBookings;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>GET /api/Bookings/all</c>, the administrative view that
/// returns every booking in the system. Verifies the broken function level authorization (BFLA)
/// contract of the endpoint (OWASP API5:2023).
/// </summary>
public class GetAllBookingsEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/Bookings/all";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GetAllBookingsEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task GetAllBookings_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync(Endpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An authenticated non-administrator must be rejected with 403 Forbidden, because the endpoint is
    /// restricted to the administrator role at the action level (OWASP API5:2023 - BFLA).
    /// </summary>
    [Fact]
    public async Task GetAllBookings_WhenAuthenticatedAsRegularUser_ReturnsForbidden()
    {
        // Arrange
        SeedBookingOwnedBy(Guid.NewGuid());

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest("User"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// An administrator must receive 200 OK with every booking in the system, including bookings owned
    /// by other users.
    /// </summary>
    [Fact]
    public async Task GetAllBookings_WhenAdministrator_ReturnsEveryBooking()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        SeedBookingOwnedBy(otherUserId);

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest("Admin"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bookings = await response.Content.ReadFromJsonAsync<List<BookingDto>>();
        bookings.Should().NotBeNull();
        bookings.Should().ContainSingle();
        bookings![0].UserId.Should().Be(otherUserId);
    }

    /// <summary>
    /// Creates an authenticated GET request carrying the supplied role.
    /// </summary>
    private static HttpRequestMessage CreateAuthenticatedRequest(string role)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, role);
        return request;
    }

    /// <summary>
    /// Persists a single booking owned by the supplied user so the administrative view has data to return.
    /// </summary>
    private void SeedBookingOwnedBy(Guid userId)
    {
        var booking = new Booking(
            userId,
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddHours(2));

        _factory.Seed(context => context.Bookings.Add(booking));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
