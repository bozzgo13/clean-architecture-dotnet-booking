using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Application.Common.Interfaces;
using MyBookingApi.Infrastructure.Persistence;
using MyBookingApi.Infrastructure.Security;

namespace MyBookingApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Register AppDbContext with SQL Server provider
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // Bind IApplicationDbContext to AppDbContext implementation
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        // Register the password hashing service used by authentication commands
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        return services;
    }
}