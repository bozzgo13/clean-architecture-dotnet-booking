using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/v1/auth/register</c>, exercising the full
/// request pipeline (routing -> controller -> MediatR validation -> handler -> EF Core).
/// </summary>
public class RegisterEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/v1/auth/register";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// A valid registration must return 201 Created and persist the user with a
    /// normalized email and a hashed password.
    /// </summary>
    [Fact]
    public async Task Register_WithValidPayload_ReturnsCreatedAndPersistsUser()
    {
        // Arrange
        var payload = new
        {
            Email = "New.User@Example.com",
            Password = "Str0ng!Pass",
            FirstName = "New",
            LastName = "User"
        };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var userId = await response.Content.ReadFromJsonAsync<Guid>();
        userId.Should().NotBeEmpty();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedUser = await context.Users.SingleOrDefaultAsync(u => u.Id == userId);

        storedUser.Should().NotBeNull();
        storedUser!.Email.Should().Be("new.user@example.com");
        storedUser.PasswordHash.Should().NotBe(payload.Password);
        storedUser.FirstName.Should().Be("New");
        storedUser.LastName.Should().Be("User");
    }

    /// <summary>
    /// Registering the same email twice (differing only by casing) must return 400 Bad Request
    /// and must not create a second account.
    /// </summary>
    [Fact]
    public async Task Register_WithDuplicateEmail_DifferentCasing_ReturnsBadRequest()
    {
        // Arrange
        var first = new { Email = "duplicate@example.com", Password = "Str0ng!Pass", FirstName = "First", LastName = "User" };
        var duplicate = new { Email = "Duplicate@example.com", Password = "Str0ng!Pass", FirstName = "Second", LastName = "User" };

        var firstResponse = await _client.PostAsJsonAsync(Endpoint, first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, duplicate);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Users.CountAsync(u => u.Email == "duplicate@example.com")).Should().Be(1);
    }

    /// <summary>
    /// A payload that violates the password policy must return 400 Bad Request
    /// (mapped from the FluentValidation pipeline behavior) and persist nothing.
    /// </summary>
    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        // Arrange: no uppercase letter, no digit and no special character.
        var payload = new
        {
            Email = "weak@example.com",
            Password = "weakpassword",
            FirstName = "Weak",
            LastName = "Password"
        };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Users.AnyAsync(u => u.Email == "weak@example.com")).Should().BeFalse();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
