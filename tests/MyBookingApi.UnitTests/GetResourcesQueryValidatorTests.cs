using FluentValidation.TestHelper;
using MyBookingApi.Application.Resources.Queries.GetResources;

/// <summary>
/// Unit tests for <see cref="GetResourcesQueryValidator"/>, verifying that abusive or malformed
/// pagination and filter parameters fail fast before any database access.
/// </summary>
public class GetResourcesQueryValidatorTests
{
    private readonly GetResourcesQueryValidator _validator = new();

    private static GetResourcesQuery ValidQuery() => new(PageNumber: 1, PageSize: 10);

    [Fact]
    public void Should_Not_Have_Error_When_Query_Is_Valid()
    {
        // Act
        var result = _validator.TestValidate(ValidQuery());

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_Have_Error_When_PageNumber_Is_Less_Than_One(int pageNumber)
    {
        var query = ValidQuery() with { PageNumber = pageNumber };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageNumber);
    }

    [Theory]
    [InlineData(GetResourcesQueryValidator.MaximumPageNumber + 1)]
    [InlineData(int.MaxValue)] // The overflow vector: an unclamped offset would wrap negative.
    public void Should_Have_Error_When_PageNumber_Exceeds_Maximum(int pageNumber)
    {
        // Arrange: an unbounded page number would overflow the (PageNumber - 1) * PageSize offset.
        var query = ValidQuery() with { PageNumber = pageNumber };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageNumber);
    }

    [Fact]
    public void Should_Not_Have_Error_When_PageNumber_Is_At_Maximum()
    {
        var query = ValidQuery() with { PageNumber = GetResourcesQueryValidator.MaximumPageNumber };

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveValidationErrorFor(x => x.PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Should_Have_Error_When_PageSize_Is_Less_Than_One(int pageSize)
    {
        var query = ValidQuery() with { PageSize = pageSize };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Should_Have_Error_When_PageSize_Exceeds_Maximum()
    {
        // Arrange: an unbounded page size would allow a client to pull the whole table at once.
        var query = ValidQuery() with { PageSize = GetResourcesQueryValidator.MaximumPageSize + 1 };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Should_Not_Have_Error_When_PageSize_Is_At_Maximum()
    {
        var query = ValidQuery() with { PageSize = GetResourcesQueryValidator.MaximumPageSize };

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Should_Not_Have_Error_When_SearchTerm_Is_Null()
    {
        // The search term is optional.
        var query = ValidQuery() with { SearchTerm = null };

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveValidationErrorFor(x => x.SearchTerm);
    }

    [Fact]
    public void Should_Have_Error_When_SearchTerm_Exceeds_Maximum_Length()
    {
        var query = ValidQuery() with
        {
            SearchTerm = new string('a', GetResourcesQueryValidator.MaximumSearchTermLength + 1)
        };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.SearchTerm);
    }
}
