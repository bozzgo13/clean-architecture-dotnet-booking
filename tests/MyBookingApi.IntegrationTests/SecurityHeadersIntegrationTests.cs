using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Integration tests asserting that the hardening response headers configured in the middleware
/// pipeline (API8:2023 - Security Misconfiguration) are emitted on responses, including error
/// responses produced by the exception handler.
/// </summary>
public class SecurityHeadersIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/v1/auth/register";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityHeadersIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Every response must carry the baseline anti-sniffing / anti-framing / referrer headers so
    /// browsers cannot be tricked into MIME sniffing or clickjacking the API.
    /// </summary>
    [Fact]
    public async Task AnyResponse_IncludesBaselineSecurityHeaders()
    {
        // Act: a deliberately invalid payload forces an error response, proving the headers are
        // applied even when the request fails (the middleware runs before the exception handler).
        var response = await _client.PostAsJsonAsync(Endpoint, new { Email = "invalid" });

        // Assert
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
