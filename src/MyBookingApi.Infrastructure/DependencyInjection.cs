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

        // Register the JWT access-token generator used by authentication commands. It only depends on
        // immutable validated options, so a single instance can safely be shared.
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Register the refresh-token generator (CSPRNG generation + SHA-256 hashing). It is stateless
        // apart from the validated options, so a single instance can safely be shared.
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();

        return services;
    }
}