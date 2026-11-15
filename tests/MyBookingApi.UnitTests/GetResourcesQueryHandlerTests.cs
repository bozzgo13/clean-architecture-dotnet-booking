using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Application.Resources.Queries.GetResources;
using MyBookingApi.Domain.Entities;
using Xunit;

/// <summary>
/// Unit tests for <see cref="GetResourcesQueryHandler"/>, verifying server-side paging, the optional
/// name/availability filters and empty-result handling against a real (in-memory) EF Core provider.
/// </summary>
public class GetResourcesQueryHandlerTests
{
    private readonly TestApplicationDbContext _context;
    private readonly GetResourcesQueryHandler _handler;

    public GetResourcesQueryHandlerTests()
    {
        // A real (in-memory) EF Core context is used instead of a mocked DbSet because the handler
        // relies on AsNoTracking()/Skip()/Take()/LINQ-to-Entities, which cannot be faithfully
        // reproduced with Moq.
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestApplicationDbContext(options);
        _handler = new GetResourcesQueryHandler(_context);
    }

    /// <summary>
    /// Creates a resource through the domain entity so its availability is applied via the behavior
    /// method rather than by mutating the private setter.
    /// </summary>
    private static Resource CreateResource(string name, bool isAvailable = true)
    {
        var resource = new Resource(name, $"{name} description");
        resource.SetAvailability(isAvailable);
        return resource;
    }

    private async Task SeedResourcesAsync(params Resource[] resources)
    {
        _context.Resources.AddRange(resources);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Tests that the first page is returned together with accurate paging metadata.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnRequestedPageWithMetadata()
    {
        // Arrange
        await SeedResourcesAsync(
            CreateResource("Resource-01"),
            CreateResource("Resource-02"),
            CreateResource("Resource-03"),
            CreateResource("Resource-04"),
            CreateResource("Resource-05"));

        var query = new GetResourcesQuery(PageNumber: 1, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.Name).Should().Equal("Resource-01", "Resource-02");
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    /// <summary>
    /// Tests that a page after the first skips the preceding items and reports the correct boundaries.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReturnLastPageCorrectly()
    {
        // Arrange
        await SeedResourcesAsync(
            CreateResource("Resource-01"),
            CreateResource("Resource-02"),
            CreateResource("Resource-03"),
            CreateResource("Resource-04"),
            CreateResource("Resource-05"));

        var query = new GetResourcesQuery(PageNumber: 3, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert: the third page holds only the remaining item.
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Resource-05");
        result.HasPreviousPage.Should().BeTrue();
        result.HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// Tests that requesting a page beyond the available range returns an empty page rather than
    /// throwing, while still reporting the total number of matches.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageBeyondRange_ShouldReturnEmptyPageWithTotalCount()
    {
        // Arrange
        await SeedResourcesAsync(CreateResource("Resource-01"), CreateResource("Resource-02"));

        var query = new GetResourcesQuery(PageNumber: 10, PageSize: 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(2);
        result.HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// Tests that the optional search term filters the result set by resource name.
    /// </summary>
    [Fact]
    public async Task Handle_WithSearchTerm_ShouldFilterByName()
    {
        // Arrange
        await SeedResourcesAsync(
            CreateResource("ConfRoom-A"),
            CreateResource("ConfRoom-B"),
            CreateResource("Laptop-01"));

        var query = new GetResourcesQuery(PageNumber: 1, PageSize: 10, SearchTerm: "ConfRoom");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(r => r.Name.Contains("ConfRoom"));
        result.TotalCount.Should().Be(2);
    }

    /// <summary>
    /// Tests that the optional availability filter returns only resources matching the requested state.
    /// </summary>
    [Fact]
    public async Task Handle_WithAvailabilityFilter_ShouldReturnOnlyMatchingResources()
    {
        // Arrange
        await SeedResourcesAsync(
            CreateResource("ConfRoom-A", isAvailable: true),
            CreateResource("ConfRoom-B", isAvailable: false),
            CreateResource("ConfRoom-C", isAvailable: false));

        var query = new GetResourcesQuery(PageNumber: 1, PageSize: 10, IsAvailable: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(r => !r.IsAvailable);
    }

    /// <summary>
    /// Tests that the search filter and paging compose correctly: the total reflects the filtered set
    /// and the page returns only the matching slice.
    /// </summary>
    [Fact]
    public async Task Handle_WithSearchTermAndPaging_ShouldPageOverFilteredResults()
    {
        // Arrange: five "Desk-" resources and two unrelated ones.
        await SeedResourcesAsync(
            CreateResource("Desk-01"),
            CreateResource("Desk-02"),
            CreateResource("Desk-03"),
            CreateResource("Desk-04"),
            CreateResource("Desk-05"),
            CreateResource("Room-01"),
            CreateResource("Room-02"));

        var query = new GetResourcesQuery(PageNumber: 2, PageSize: 2, SearchTerm: "Desk");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert: the filter is applied before paging, so the page holds the 3rd and 4th matches.
        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.Name).Should().Equal("Desk-03", "Desk-04");
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    /// <summary>
    /// Tests that a filter matching nothing yields an empty page with a zero total instead of an error.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoResourcesMatch_ShouldReturnEmptyPage()
    {
        // Arrange
        await SeedResourcesAsync(CreateResource("ConfRoom-A"));

        var query = new GetResourcesQuery(PageNumber: 1, PageSize: 10, SearchTerm: "Nonexistent");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// Minimal EF Core context used to exercise the handler against a real query provider
    /// without depending on the Infrastructure layer.
    /// </summary>
    private class TestApplicationDbContext : DbContext, IApplicationDbContext
    {
        public TestApplicationDbContext(DbContextOptions<TestApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Resource> Resources => Set<Resource>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    }
}
