using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Domain.Enums;
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
            Email = "New.User@email.com",
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
        storedUser!.Email.Should().Be("new.user@email.com");
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
        var first = new { Email = "duplicate@email.com", Password = "Str0ng!Pass", FirstName = "First", LastName = "User" };
        var duplicate = new { Email = "Duplicate@email.com", Password = "Str0ng!Pass", FirstName = "Second", LastName = "User" };

        var firstResponse = await _client.PostAsJsonAsync(Endpoint, first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, duplicate);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Users.CountAsync(u => u.Email == "duplicate@email.com")).Should().Be(1);
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
            Email = "weak@email.com",
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
        (await context.Users.AnyAsync(u => u.Email == "weak@email.com")).Should().BeFalse();
    }

    /// <summary>
    /// A payload with an over-long password must be rejected with 400 Bad Request, guarding the
    /// password-hashing step against resource-exhaustion attempts using oversized inputs.
    /// </summary>
    [Fact]
    public async Task Register_WithOverlongPassword_ReturnsBadRequest()
    {
        // Arrange: 128 valid characters followed by extra padding, exceeding the configured maximum.
        var payload = new
        {
            Email = "longpass@email.com",
            Password = "A" + new string('a', 200) + "1!",
            FirstName = "First",
            LastName = "Last"
        };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await context.Users.AnyAsync(u => u.Email == "longpass@email.com")).Should().BeFalse();
    }

    /// <summary>
    /// Validation errors must be returned as an RFC 7807 ProblemDetails payload that carries a
    /// correlation identifier but no internal detail.
    /// </summary>
    [Fact]
    public async Task Register_WithInvalidPayload_ReturnsProblemDetailsWithTraceId()
    {
        // Arrange
        var payload = new { Email = "not-an-email", Password = "short", FirstName = "", LastName = "" };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The registration endpoint must be throttled: once the configured permit limit is exceeded the
    /// service must respond with 429 Too Many Requests to slow automated abuse and enumeration.
    /// </summary>
    [Fact]
    public async Task Register_WhenRateLimitExceeded_ReturnsTooManyRequests()
    {
        // Arrange: the "registration" policy permits five requests per minute per client.
        var responses = new List<HttpResponseMessage>();

        // Act
        for (var i = 0; i < 6; i++)
        {
            var payload = new
            {
                Email = $"x.uporabnik{i}@email.com",
                Password = $"xX{i}123{i}!@",
                FirstName = $"Uporabnik{i}",
                LastName = "X"
            };

            responses.Add(await _client.PostAsJsonAsync(Endpoint, payload));
        }

        // Assert: the first five are accepted and the sixth is throttled.
        responses.Take(5).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        responses[5].StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Mass-assignment (API3): a client that injects privileged fields ("role": "Admin",
    /// "isAdmin": true) into the registration payload must not be able to elevate the created
    /// account. The persisted user keeps the default non-privileged role.
    /// </summary>
    [Fact]
    public async Task Register_WithOverpostedPrivilegedFields_DoesNotElevateRole()
    {
        // Arrange: the extra fields do not exist on the command, so they are ignored on binding.
        var payload = new
        {
            Email = "overpost.role@email.com",
            Password = "Px11@!451",
            FirstName = "Over",
            LastName = "Post",
            role = "Admin",
            isAdmin = true
        };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var userId = await response.Content.ReadFromJsonAsync<Guid>();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedUser = await context.Users.SingleAsync(u => u.Id == userId);

        storedUser.Role.Should().Be(UserRole.User);
    }

    /// <summary>
    /// Data minimization (API3): the registration response must not echo the submitted password or
    /// the stored hash; it only returns the opaque user identifier.
    /// </summary>
    [Fact]
    public async Task Register_WithValidPayload_DoesNotExposeCredentials()
    {
        // Arrange
        var payload = new
        {
            Email = "DoesNotExposeCredentials@email.com",
            Password = "Xc23@!000",
            FirstName = "DoesNot",
            LastName = "ExposeCredentials"
        };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(payload.Password);
        body.Should().NotContain("PasswordHash");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
