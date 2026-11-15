using FluentValidation;

namespace MyBookingApi.Application.Resources.Queries.GetResources;

/// <summary>
/// Defines validation rules for the <see cref="GetResourcesQuery"/> pagination and filter parameters
/// so abusive or malformed input is rejected before any database access (fail-fast boundary).
/// </summary>
public class GetResourcesQueryValidator : AbstractValidator<GetResourcesQuery>
{
    /// <summary>
    /// The page size applied by the API when the client does not request one explicitly.
    /// </summary>
    public const int DefaultPageSize = 10;

    /// <summary>
    /// The hard upper bound on the page size. It caps how much data a single request can pull from the
    /// database, preventing a client from requesting an unbounded result set (OWASP API4:2023 -
    /// Unrestricted Resource Consumption).
    /// </summary>
    public const int MaximumPageSize = 100;

    /// <summary>
    /// The hard upper bound on the page number. Together with <see cref="MaximumPageSize"/> it keeps the
    /// computed OFFSET (<c>(PageNumber - 1) * PageSize</c>) within <see cref="int"/> range, so a crafted
    /// page number cannot overflow the offset into a negative value and trigger a server error, and it
    /// bounds the cost of deep pagination (OWASP API4:2023 - Unrestricted Resource Consumption).
    /// </summary>
    public const int MaximumPageNumber = 10_000;

    /// <summary>
    /// The maximum accepted length of the search term. Mirrors the resource-name column bound so an
    /// oversized filter cannot be used to consume disproportionate server resources.
    /// </summary>
    public const int MaximumSearchTermLength = 150;

    public GetResourcesQueryValidator()
    {
        RuleFor(v => v.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("PageNumber must be greater than or equal to 1.")
            .LessThanOrEqualTo(MaximumPageNumber)
            .WithMessage($"PageNumber must not exceed {MaximumPageNumber}.");

        RuleFor(v => v.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage("PageSize must be greater than or equal to 1.")
            .LessThanOrEqualTo(MaximumPageSize)
            .WithMessage($"PageSize must not exceed {MaximumPageSize}.");

        // The search term is optional; only its upper bound is enforced, so an unbounded string cannot
        // reach the database even though the filter itself is parameterised.
        RuleFor(v => v.SearchTerm)
            .MaximumLength(MaximumSearchTermLength)
            .WithMessage($"SearchTerm must not exceed {MaximumSearchTermLength} characters.");
    }
}
