using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>POST /api/v1/auth/refresh</c>, exercising the full request
/// pipeline (routing -> controller -> MediatR validation -> handler -> token rotation -> global
/// ProblemDetails error handling).
/// </summary>
public class RefreshEndpointIntegrationTests : IDisposable
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RefreshEndpointIntegrationTests()
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
    /// Seeds a refresh token for the supplied user with a lifetime that has already elapsed, so the
    /// expired-token path can be exercised without waiting.
    /// </summary>
    private void SeedExpiredRefreshToken(Guid userId, string rawToken)
    {
        using var scope = _factory.Services.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IRefreshTokenGenerator>();

        var token = new RefreshToken(
            userId,
            generator.ComputeHash(rawToken),
            DateTime.UtcNow.AddDays(-2),
            DateTime.UtcNow.AddDays(-1));

        _factory.Seed(context => context.RefreshTokens.Add(token));
    }

    /// <summary>
    /// Authenticates a seeded account and returns the issued access and refresh tokens.
    /// </summary>
    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>
    /// A valid refresh token must be exchanged for a brand-new access token and a rotated refresh
    /// token (i.e. a different value than the one presented).
    /// </summary>
    [Fact]
    public async Task Refresh_WithValidToken_ReturnsRotatedTokenPair()
    {
        // Arrange
        SeedUser("rotate@email.com", "Str0ng!Pass");
        var (_, refreshToken) = await LoginAsync("rotate@email.com", "Str0ng!Pass");

        // Act
        var response = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = refreshToken });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        json.GetProperty("expiresAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow);

        var rotatedToken = json.GetProperty("refreshToken").GetString();
        rotatedToken.Should().NotBeNullOrWhiteSpace();
        rotatedToken.Should().NotBe(refreshToken);
    }

    /// <summary>
    /// A refresh token may only be redeemed once. Replaying an already-used (revoked) token must be
    /// rejected with 401 and, as a breach precaution, must invalidate every other active token for
    /// the same user.
    /// </summary>
    [Fact]
    public async Task Refresh_WhenRevokedTokenIsReplayed_ReturnsUnauthorizedAndInvalidatesAllTokens()
    {
        // Arrange: obtain a token, then rotate it once so the original becomes revoked.
        SeedUser("replay@email.com", "Str0ng!Pass");
        var (_, firstToken) = await LoginAsync("replay@email.com", "Str0ng!Pass");

        var firstRefresh = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = firstToken });
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondToken = (await firstRefresh.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString();

        // Act: replay the now-revoked first token.
        var replayed = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = firstToken });

        // Assert: the replay is rejected as a generic 401.
        await AssertGenericUnauthorized(replayed, firstToken);

        // Assert: the breach precaution revoked every active token, so even the newest token is dead.
        var afterBreach = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = secondToken });
        await AssertGenericUnauthorized(afterBreach, secondToken!);
    }

    /// <summary>
    /// An expired refresh token must be rejected with a generic 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var userId = SeedUser("expired@email.com", "Str0ng!Pass");
        const string expiredToken = "expired-raw-refresh-token";
        SeedExpiredRefreshToken(userId, expiredToken);

        // Act
        var response = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = expiredToken });

        // Assert
        await AssertGenericUnauthorized(response, expiredToken);
    }

    /// <summary>
    /// A token that was never issued must be rejected with the same generic 401 Unauthorized, so the
    /// endpoint reveals nothing about which tokens exist.
    /// </summary>
    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        // Arrange
        SeedUser("unknown@email.com", "Str0ng!Pass");
        const string unknownToken = "never-issued-refresh-token";

        // Act
        var response = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = unknownToken });

        // Assert
        await AssertGenericUnauthorized(response, unknownToken);
    }

    /// <summary>
    /// A payload that fails validation must return 400 Bad Request as application/problem+json with a
    /// correlation identifier, without ever reaching the database.
    /// </summary>
    [Fact]
    public async Task Refresh_WithInvalidPayload_ReturnsProblemDetailsWithTraceId()
    {
        // Arrange
        var payload = new { RefreshToken = "" };

        // Act
        var response = await _client.PostAsJsonAsync(RefreshEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The raw refresh token must never be persisted: only its hash is stored, so a database
    /// disclosure yields no usable credential.
    /// </summary>
    [Fact]
    public async Task Refresh_ShouldNeverPersistTheRawToken()
    {
        // Arrange
        SeedUser("nohash@email.com", "Str0ng!Pass");
        var (_, refreshToken) = await LoginAsync("nohash@email.com", "Str0ng!Pass");

        // Act
        var response = await _client.PostAsJsonAsync(RefreshEndpoint, new { RefreshToken = refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: the stored hashes never equal the raw token value.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MyBookingApi.Infrastructure.Persistence.AppDbContext>();
        var storedHashes = await context.RefreshTokens.Select(t => t.TokenHash).ToListAsync();

        storedHashes.Should().NotBeEmpty();
        storedHashes.Should().NotContain(refreshToken);
    }

    /// <summary>
    /// Asserts that a response is the generic 401 Unauthorized problem payload and that it leaks the
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
