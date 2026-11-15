namespace MyBookingApi.Application.Common.Models;

/// <summary>
/// Generic pagination envelope returned by list queries. It carries only the current page of
/// read models together with the metadata a client needs to page through the collection, so a
/// bounded server-side query can be represented without ever exposing the underlying entities
/// (OWASP API3:2023 - Excessive Data Exposure).
/// </summary>
/// <typeparam name="T">The read-model type contained in the page.</typeparam>
/// <param name="Items">The read models belonging to the requested page.</param>
/// <param name="PageNumber">The 1-based number of the returned page.</param>
/// <param name="PageSize">The maximum number of items requested per page.</param>
/// <param name="TotalCount">The total number of items matching the filters, across every page.</param>
public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    /// <summary>
    /// Gets the total number of pages available for the requested <see cref="PageSize"/>.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// Gets a value indicating whether a page preceding <see cref="PageNumber"/> exists.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Gets a value indicating whether a page following <see cref="PageNumber"/> exists.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;
}
