using MediatR;
using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Application.Common.Models;
using MyBookingApi.Application.Resources.Commands.CreateResource;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Resources.Queries.GetResources;

/// <summary>
/// Represents a CQRS Query that retrieves a single page of resources, optionally filtered by a name
/// search term and/or availability. Implements MediatR's IRequest to return a
/// <see cref="PagedResponse{T}"/> of <see cref="ResourceDto"/> read models.
/// </summary>
/// <param name="PageNumber">The 1-based page to return.</param>
/// <param name="PageSize">The number of items per page. Bounded by <c>GetResourcesQueryValidator</c>.</param>
/// <param name="SearchTerm">An optional substring matched against the resource name; <c>null</c> disables the filter.</param>
/// <param name="IsAvailable">An optional availability filter; <c>null</c> disables the filter.</param>
public record GetResourcesQuery(
    int PageNumber,
    int PageSize,
    string? SearchTerm = null,
    bool? IsAvailable = null
) : IRequest<PagedResponse<ResourceDto>>;

/// <summary>
/// Handles the execution of the <see cref="GetResourcesQuery"/>.
/// Applies the optional filters, counts the matches and returns a server-side paged projection.
/// </summary>
public class GetResourcesQueryHandler : IRequestHandler<GetResourcesQuery, PagedResponse<ResourceDto>>
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetResourcesQueryHandler"/> class.
    /// </summary>
    /// <param name="context">The database context abstraction accessed via Dependency Injection.</param>
    public GetResourcesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves the requested page of resources asynchronously, projected into <see cref="ResourceDto"/> instances.
    /// </summary>
    /// <param name="request">The query describing the pagination and filtering to apply.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>A <see cref="PagedResponse{T}"/> describing the requested page and the total number of matches.</returns>
    public async Task<PagedResponse<ResourceDto>> Handle(GetResourcesQuery request, CancellationToken cancellationToken)
    {
        // Read-only query: AsNoTracking avoids the change-tracker overhead for data we never modify.
        IQueryable<Resource> query = _context.Resources.AsNoTracking();

        // Optional filters are only translated into SQL when a value was actually supplied, keeping the
        // unfiltered listing fully index-friendly.
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.Trim();
            query = query.Where(r => r.Name.Contains(searchTerm));
        }

        if (request.IsAvailable.HasValue)
        {
            query = query.Where(r => r.IsAvailable == request.IsAvailable.Value);
        }

        // The total is counted before paging so the client can compute the number of pages; it is a
        // cheap COUNT over the filtered set rather than materialising every row.
        var totalCount = await query.CountAsync(cancellationToken);

        // Server-side paging (Skip/Take) bounds the number of transferred rows (OWASP API4:2023 -
        // Unrestricted Resource Consumption) and the projection returns only allow-listed fields
        // (OWASP API3:2023 - Excessive Data Exposure). A stable secondary ordering is unnecessary
        // because Resource.Id is unique, but ordering by name gives a deterministic, human-friendly page.
        var items = await query
            .OrderBy(r => r.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ResourceDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsAvailable))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ResourceDto>(items, request.PageNumber, request.PageSize, totalCount);
    }
}
