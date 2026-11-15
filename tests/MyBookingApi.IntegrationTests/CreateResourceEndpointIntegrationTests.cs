using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Domain.Entities;
using MyBookingApi.Infrastructure.Persistence;
using MyBookingApi.WebApi.Contracts;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/v1/resources</c>, exercising the full request
/// pipeline (authentication -> role authorization -> controller -> MediatR -> validation -> EF Core)
/// and the OWASP API1/API3/API5/API6 controls attached to it.
/// </summary>
public class CreateResourceEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/v1/resources";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CreateResourceEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task Create_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var payload = new CreateResourceRequest("ConfRoom-A:1F", "First-floor conference room A");

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An authenticated non-administrator must be rejected with 403 Forbidden (OWASP API5:2023 - BFLA):
    /// the endpoint is administrative, so a regular user is not allowed to create resources.
    /// </summary>
    [Fact]
    public async Task Create_WhenAuthenticatedAsRegularUser_ReturnsForbidden()
    {
        // Arrange
        var payload = new CreateResourceRequest("ConfRoom-A:1F", "First-floor conference room A");

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Nothing may be persisted when authorization fails.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Resources.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// An administrator posting a valid payload must receive 201 Created, a Location header pointing at
    /// the new resource and the created DTO, and the record must be persisted in the database.
    /// </summary>
    [Fact]
    public async Task Create_WhenAuthenticatedAsAdmin_ReturnsCreatedAndPersistsResource()
    {
        // Arrange
        var payload = new CreateResourceRequest("ConfRoom-A:1F", "First-floor conference room A");

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ResourceDto>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be("ConfRoom-A:1F");
        created.Description.Should().Be("First-floor conference room A");
        created.IsAvailable.Should().BeTrue();

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be($"{Endpoint}/{created.Id}");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedResource = await context.Resources.SingleAsync(r => r.Id == created.Id);

        storedResource.Name.Should().Be("ConfRoom-A:1F");
        storedResource.Description.Should().Be("First-floor conference room A");
        storedResource.IsAvailable.Should().BeTrue();
    }

    /// <summary>
    /// When the payload omits <c>isActive</c> the resource is created as available, which keeps the
    /// default opt-out behavior instead of silently creating unusable resources.
    /// </summary>
    [Fact]
    public async Task Create_WithOmittedIsActive_DefaultsToAvailable()
    {
        // Arrange: only the required field is supplied.
        var payload = new { Name = "ConfRoom-B:2F" };

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ResourceDto>();
        created!.IsAvailable.Should().BeTrue();
    }

    /// <summary>
    /// An administrator explicitly creating an inactive resource must have the availability honoured.
    /// </summary>
    [Fact]
    public async Task Create_WithIsActiveFalse_ReturnsCreatedAndPersistsUnavailableResource()
    {
        // Arrange
        var payload = new CreateResourceRequest("ConfRoom-C:3F", "Out-of-service conference room", IsActive: false);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedResource = await context.Resources.SingleAsync(r => r.Name == "ConfRoom-C:3F");

        storedResource.IsAvailable.Should().BeFalse();
    }

    /// <summary>
    /// Creating a second resource with an already-used name must fail the uniqueness business rule and
    /// return 400 Bad Request (OWASP API6:2023 - Unrestricted Access to Sensitive Business Flows).
    /// </summary>
    [Fact]
    public async Task Create_WithDuplicateName_ReturnsBadRequest()
    {
        // Arrange: seed an existing resource with the same name.
        _factory.Seed(context => context.Resources.Add(new Resource("ConfRoom-A:1F", "Existing resource")));

        var payload = new CreateResourceRequest("ConfRoom-A:1F", "A different description");

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Resources.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// An authenticated administrative request with an invalid payload must fail validation and be
    /// mapped to 400 Bad Request by the global exception handler (ValidationBehavior).
    /// </summary>
    [Fact]
    public async Task Create_WithInvalidPayload_ReturnsBadRequest()
    {
        // Arrange: an empty name fails the CreateResourceCommandValidator rules.
        var payload = new CreateResourceRequest(string.Empty, "Missing name");

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Mass-assignment (API3): a client injecting privileged/server-owned fields (id, isAvailable)
    /// must not influence server-side state. The identifier is generated by the server and the
    /// availability is derived from the allow-listed input only.
    /// </summary>
    [Fact]
    public async Task Create_WithOverpostedStateFields_IgnoresThem()
    {
        // Arrange
        var clientSuppliedId = Guid.NewGuid();
        var overpostedPayload = new
        {
            Id = clientSuppliedId,
            Name = "ConfRoom-D:4F",
            Description = "Overposting attempt",
            IsAvailable = false,
            IsActive = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(overpostedPayload)
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ResourceDto>();
        created!.Id.Should().NotBe(clientSuppliedId);
        created.IsAvailable.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Resources.AnyAsync(r => r.Id == clientSuppliedId)).Should().BeFalse();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
