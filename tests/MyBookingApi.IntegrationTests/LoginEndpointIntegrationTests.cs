using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/v1/auth/login</c>, exercising the full request
/// pipeline (routing -> controller -> MediatR validation -> handler -> password verification ->
/// token issuance -> global ProblemDetails error handling).
/// </summary>
public class LoginEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/v1/auth/login";

    // Mirrors the value injected by MyBookingApiWebApplicationFactory so the signed claims can be asserted.
    private const string Issuer = "MyBookingApi";
    private const string Audience = "MyBookingApiClient";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoginEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Seeds an account using the real password hasher so the login flow verifies against a genuine
    /// hash, and returns the created user's identifier.
    /// </summary>
    private Guid SeedUser(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User(email, hasher.HashPassword(password), "Test", "User");
        _factory.Seed(context => context.Users.Add(user));

        return user.Id;
    }

    /// <summary>
    /// Valid credentials must return 200 OK with a signed access token and a refresh token.
    /// </summary>
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        // Arrange
        var userId = SeedUser("valid.login@email.com", "Str0ng!Pass");
        var payload = new { Email = "valid.login@email.com", Password = "Str0ng!Pass" };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var accessToken = json.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrWhiteSpace();

        json.GetProperty("expiresAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow);
        json.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();

        // The access token is a real, correctly issued JWT carrying the authenticated user's identity.
        var claims = DecodeJwtPayload(accessToken!);
        claims.GetProperty("iss").GetString().Should().Be(Issuer);
        claims.GetProperty("aud").GetString().Should().Be(Audience);
        claims.EnumerateObject()
            .Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null)
            .Should().Contain(userId.ToString());
    }

    /// <summary>
    /// The response must not echo the submitted password or expose any stored hash (data minimization).
    /// </summary>
    [Fact]
    public async Task Login_WithValidCredentials_DoesNotExposeCredentials()
    {
        // Arrange
        SeedUser("no.leak@email.com", "Xc23@!000");
        var payload = new { Email = "no.leak@email.com", Password = "Xc23@!000" };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain("Xc23@!000");
        body.Should().NotContain("PasswordHash");
    }

    /// <summary>
    /// An unknown email must produce a generic 401 Unauthorized as an RFC 7807 ProblemDetails payload.
    /// </summary>
    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsGenericUnauthorized()
    {
        // Arrange
        var payload = new { Email = "ghost@email.com", Password = "Str0ng!Pass" };

        // Act
        var response = await _client.PostAsJsonAsync(Endpoint, payload);

        // Assert
        await AssertGenericUnauthorized(response, payload.Password);
    }

    /// <summary>
    /// A wrong password must produce an indistinguishable 401 Unauthorized, so the endpoint cannot be
    /// used to enumerate valid accounts (OWASP API2:2023 - Broken Authentication).
    /// </summary>
    [Fact]
    public async Task Login_WithWrongPassword_ReturnsSameUnauthorizedAsUnknownEmail()
    {
        // Arrange
        SeedUser("existing.user@email.com", "Str0ng!Pass");

        var unknownEmail = await _client.PostAsJsonAsync(Endpoint, new { Email = "ghost@email.com", Password = "Str0ng!Pass" });
        var wrongPassword = await _client.PostAsJsonAsync(Endpoint, new { Email = "existing.user@email.com", Password = "Wr0ng!Pass" });

        // Assert: identical status and ProblemDetails body - no account-enumeration signal.
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unknownProblem = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>();
        var wrongProblem = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();

        wrongProblem.GetProperty("title").GetString().Should().Be(unknownProblem.GetProperty("title").GetString());
        wrongProblem.GetProperty("detail").GetString().Should().Be(unknownProblem.GetProperty("detail").GetString());

        foreach (var response in new[] { unknownEmail, wrongPassword })
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// A payload that fails validation must return 400 Bad Request as application/problem+json with a
    /// correlation identifier, without ever reaching the database.
    /// </summary>
    [Fact]
    public async Task Login_WithInvalidPayload_ReturnsProblemDetailsWithTraceId()
    {
        // Arrange
        var payload = new { Email = "not-an-email", Password = "" };

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
    /// The login endpoint must be throttled: once the configured permit limit is exceeded the service
    /// must respond with 429 Too Many Requests to slow automated brute-force attempts.
    /// </summary>
    [Fact]
    public async Task Login_WhenRateLimitExceeded_ReturnsTooManyRequests()
    {
        // Arrange: the "login" policy permits five requests per minute per client.
        var responses = new List<HttpResponseMessage>();

        // Act
        for (var i = 0; i < 6; i++)
        {
            responses.Add(await _client.PostAsJsonAsync(Endpoint, new { Email = "throttle@email.com", Password = "Str0ng!Pass" }));
        }

        // Assert: the first five are processed (401 - unknown account) and the sixth is throttled.
        responses.Take(5).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);
        responses[5].StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Asserts that a response is the generic 401 Unauthorized problem payload and that it leaks
    /// neither the submitted password nor any account detail.
    /// </summary>
    private static async Task AssertGenericUnauthorized(HttpResponseMessage response, string submittedPassword)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(submittedPassword);

        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("status").GetInt32().Should().Be(401);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Decodes the payload segment of a JWT without validating its signature (the host under test uses
    /// a deterministic authentication scheme), so the issued claims can be inspected.
    /// </summary>
    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));

        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
