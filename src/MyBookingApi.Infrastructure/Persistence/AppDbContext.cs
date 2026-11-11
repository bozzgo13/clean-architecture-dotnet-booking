using Microsoft.EntityFrameworkCore;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Domain.Entities;
using System.Reflection;

namespace MyBookingApi.Infrastructure.Persistence;

/// <summary>
/// Implements the database context for the persistence layer using Entity Framework Core.
/// Satisfies the IApplicationDbContext contract defined in the Application layer.
/// </summary>
public class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Automatically apply all IEntityTypeConfiguration configurations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(modelBuilder);
    }
}