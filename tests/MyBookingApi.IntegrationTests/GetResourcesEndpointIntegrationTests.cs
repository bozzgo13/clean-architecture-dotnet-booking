using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// End-to-end HTTP integration tests for <c>GET /api/v1/resources</c>, exercising the full request
/// pipeline (authentication -> controller -> MediatR validation -> handler -> EF Core) including
/// server-side paging, filtering, pagination validation and data minimization (OWASP API3/API4).
/// </summary>
public class GetResourcesEndpointIntegrationTests : IDisposable
{
    private const string Endpoint = "/api/v1/resources";

    private readonly MyBookingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GetResourcesEndpointIntegrationTests()
    {
        _factory = new MyBookingApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Builds a resource through the domain entity so its availability is applied via the behavior method.
    /// </summary>
    private static Resource CreateResource(string name, bool isAvailable = true)
    {
        var resource = new Resource(name, $"{name} description");
        resource.SetAvailability(isAvailable);
        return resource;
    }

    /// <summary>
    /// Seeds five deterministically named resources so paging assertions are stable.
    /// </summary>
    private void SeedFiveResources()
    {
        _factory.Seed(context =>
        {
            for (var i = 1; i <= 5; i++)
            {
                context.Resources.Add(CreateResource($"Resource-{i:00}"));
            }
        });
    }

    /// <summary>
    /// Creates an authenticated GET request acting as a standard (non-administrator) user. The listing
    /// endpoint is available to every authenticated caller, so this role must reach the handler.
    /// </summary>
    private static HttpRequestMessage CreateAuthenticatedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.RoleHeader, "User");
        return request;
    }

    /// <summary>
    /// Local projection of the paginated response envelope used to assert the paging metadata.
    /// </summary>
    private sealed record PagedResources(
        List<ResourceDto> Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    /// <summary>
    /// An unauthenticated request must be rejected with 401 Unauthorized before reaching the handler.
    /// </summary>
    [Fact]
    public async Task GetResources_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync(Endpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// An authenticated standard (non-administrator) user must be allowed to list resources, because
    /// the endpoint is available to every logged-in user (OWASP API5:2023 - BFLA).
    /// </summary>
    [Fact]
    public async Task GetResources_WhenStandardUser_ReturnsOk()
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest(Endpoint));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// An authenticated user must receive 200 OK with the first page and accurate paging metadata.
    /// </summary>
    [Fact]
    public async Task GetResources_WithPaging_ReturnsRequestedPageAndMetadata()
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?pageNumber=1&pageSize=2"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResources>();
        page.Should().NotBeNull();
        page!.Items.Should().HaveCount(2);
        page.Items.Select(r => r.Name).Should().Equal("Resource-01", "Resource-02");
        page.PageNumber.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
        page.HasPreviousPage.Should().BeFalse();
        page.HasNextPage.Should().BeTrue();
    }

    /// <summary>
    /// The default paging (no query string) must return up to ten items per page, applying the
    /// documented defaults rather than an unbounded result set.
    /// </summary>
    [Fact]
    public async Task GetResources_WithoutParameters_AppliesDefaultPaging()
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest(Endpoint));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResources>();
        page!.PageNumber.Should().Be(1);
        page.PageSize.Should().Be(10);
        page.TotalCount.Should().Be(5);
        page.Items.Should().HaveCount(5);
    }

    /// <summary>
    /// Requesting a page beyond the available range must return 200 OK with an empty item list and the
    /// correct total, rather than an error.
    /// </summary>
    [Fact]
    public async Task GetResources_WhenPageBeyondRange_ReturnsEmptyPageWithMetadata()
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?pageNumber=10&pageSize=2"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResources>();
        page!.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(5);
        page.HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// The optional search term must filter the result set by resource name.
    /// </summary>
    [Fact]
    public async Task GetResources_WithSearchTerm_ReturnsOnlyMatchingResources()
    {
        // Arrange
        _factory.Seed(context =>
        {
            context.Resources.Add(CreateResource("ConfRoom-A"));
            context.Resources.Add(CreateResource("ConfRoom-B"));
            context.Resources.Add(CreateResource("Laptop-01"));
        });

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?searchTerm=ConfRoom"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResources>();
        page!.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(r => r.Name.Contains("ConfRoom"));
        page.TotalCount.Should().Be(2);
    }

    /// <summary>
    /// The optional availability filter must return only resources matching the requested state.
    /// </summary>
    [Fact]
    public async Task GetResources_WithUnavailableFilter_ReturnsOnlyUnavailableResources()
    {
        // Arrange
        _factory.Seed(context =>
        {
            context.Resources.Add(CreateResource("ConfRoom-A", isAvailable: true));
            context.Resources.Add(CreateResource("ConfRoom-B", isAvailable: false));
        });

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?isAvailable=false"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResources>();
        page!.Items.Should().ContainSingle();
        page.Items.Should().OnlyContain(r => !r.IsAvailable);
    }

    /// <summary>
    /// Invalid pagination values must be rejected with 400 Bad Request by the FluentValidation pipeline
    /// before any data is queried (OWASP API4:2023 - Unrestricted Resource Consumption).
    /// </summary>
    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageNumber=-1")]
    [InlineData("pageNumber=10001")]
    [InlineData("pageNumber=2147483647")] // int.MaxValue: would overflow the offset if unclamped.
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-5")]
    [InlineData("pageSize=101")]
    public async Task GetResources_WithInvalidPagination_ReturnsBadRequest(string queryString)
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?{queryString}"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// The response must expose only the allow-listed read-model fields, never the domain entity's
    /// members (OWASP API3:2023 - Excessive Data Exposure).
    /// </summary>
    [Fact]
    public async Task GetResources_ResponseExposesOnlyAllowListedFields()
    {
        // Arrange
        SeedFiveResources();

        // Act
        var response = await _client.SendAsync(CreateAuthenticatedRequest($"{Endpoint}?pageNumber=1&pageSize=1"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // The envelope carries only the paging metadata.
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "items", "pageNumber", "pageSize", "totalCount", "totalPages", "hasPreviousPage", "hasNextPage"
        });

        // Each item exposes only the read-model fields.
        var item = root.GetProperty("items")[0];
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "id", "name", "description", "isAvailable"
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
