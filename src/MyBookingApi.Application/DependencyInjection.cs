using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using FluentValidation;

namespace MyBookingApi.Application;

/// <summary>
/// Provides extension methods for setting up Application layer services in the Dependency Injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Application layer services (MediatR handlers, FluentValidation, etc.) into the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register MediatR handlers from the current assembly
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        // Register FluentValidation validators from the current assembly
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}