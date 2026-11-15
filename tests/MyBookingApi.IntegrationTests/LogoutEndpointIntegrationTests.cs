using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/v1/auth/logout</c>, exercising the full request
/// pipeline (routing -> controller -> MediatR validation -> handler -> token revocation -> global
/// ProblemDetails error handling).
/// </summary>
public class LogoutEndpointIntegrationTests : IDisposable
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string LogoutEndpoint = "/api/v1/auth/logout";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LogoutEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Seeds an account using the real password hasher so the login flow verifies against a genuine
    /// hash.
    /// </summary>
    private void SeedUser(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User(email, hasher.HashPassword(password), "Test", "User");
        _factory.Seed(context => context.Users.Add(user));
    }

    /// <summary>
    /// Authenticates a seeded account and returns the issued refresh token.
    /// </summary>
    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("refreshToken").GetString()!;
    }

    /// <summary>
    /// A successful logout must revoke the presented refresh token so it can no longer be exchanged
    /// for a new access token.
    /// </summary>
    [Fact]
    public async Task Logout_WithValidToken_RevokesTheToken()
    {
        // Arrange
        SeedUser("logout@email.com", "Str0ng!Pass");
        var refreshToken = await LoginAsync("logout@email.com", "Str0ng!Pass");

        // Act
        var response = await _client.PostAsJsonAsync(LogoutEndpoint, new { RefreshToken = refreshToken });

        // Assert: sign-out succeeds with an empty body.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: the revoked token can no longer be refreshed.
        var refreshAttempt = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = refreshToken });
        await AssertGenericUnauthorized(refreshAttempt, refreshToken);
    }

    /// <summary>
    /// Logout only ends the session that presented the token: other active sessions stay valid.
    /// </summary>
    [Fact]
    public async Task Logout_ShouldNotRevokeOtherSessions()
    {
        // Arrange: two independent sessions (two logins) for the same account.
        SeedUser("multi@email.com", "Str0ng!Pass");
        var firstSession = await LoginAsync("multi@email.com", "Str0ng!Pass");
        var secondSession = await LoginAsync("multi@email.com", "Str0ng!Pass");

        // Act: sign out of the first session only.
        var logout = await _client.PostAsJsonAsync(LogoutEndpoint, new { RefreshToken = firstSession });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: the second session can still be rotated.
        var refreshAttempt = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = secondSession });
        refreshAttempt.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A token that was never issued must still produce a successful response, so the endpoint cannot
    /// be used to probe which token values exist.
    /// </summary>
    [Fact]
    public async Task Logout_WithUnknownToken_ReturnsNoContent()
    {
        // Arrange
        SeedUser("unknown-logout@email.com", "Str0ng!Pass");
        const string unknownToken = "never-issued-refresh-token";

        // Act
        var response = await _client.PostAsJsonAsync(LogoutEndpoint, new { RefreshToken = unknownToken });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Signing out twice must be safe: the second call is an idempotent success.
    /// </summary>
    [Fact]
    public async Task Logout_Repeated_IsIdempotent()
    {
        // Arrange
        SeedUser("repeat@email.com", "Str0ng!Pass");
        var refreshToken = await LoginAsync("repeat@email.com", "Str0ng!Pass");

        // Act
        var first = await _client.PostAsJsonAsync(LogoutEndpoint, new { RefreshToken = refreshToken });
        var second = await _client.PostAsJsonAsync(LogoutEndpoint, new { RefreshToken = refreshToken });

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A payload that fails validation must return 400 Bad Request as application/problem+json with a
    /// correlation identifier, without ever reaching the database.
    /// </summary>
    [Fact]
    public async Task Logout_WithInvalidPayload_ReturnsProblemDetailsWithTraceId()
    {
        // Arrange
        var payload = new { RefreshToken = "" };

        // Act
        var response = await _client.PostAsJsonAsync(LogoutEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Asserts that a response is the generic 401 Unauthorized problem payload that does not leak the
    /// submitted token value.
    /// </summary>
    private static async Task AssertGenericUnauthorized(HttpResponseMessage response, string submittedToken)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(submittedToken);

        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("status").GetInt32().Should().Be(401);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
