using Microsoft.EntityFrameworkCore;
using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Common.Interfaces;

/// <summary>
/// Defines the database context contract for the Application layer.
/// 
/// By defining this abstraction here, we invert the dependency: the Infrastructure layer will implement 
/// this interface, allowing the Application layer to orchestrate persistence operations without knowing 
/// how or where the data is actually stored.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>
    /// Gets the DbSet for system users.
    /// </summary>
    DbSet<User> Users { get; }

    /// <summary>
    /// Gets the DbSet for reservable resources/equipment.
    /// </summary>
    DbSet<Resource> Resources { get; }

    /// <summary>
    /// Gets the DbSet for bookings.
    /// </summary>
    DbSet<Booking> Bookings { get; }

    /// <summary>
    /// Asynchronously saves all changes made in this context to the underlying database.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}