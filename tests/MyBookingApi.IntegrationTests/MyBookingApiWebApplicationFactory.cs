using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Infrastructure.Persistence;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that boots the real Web API pipeline
/// (controllers, middleware, authorization) while swapping external dependencies for test doubles:
/// the SQL Server database is replaced by an isolated in-memory database and the JWT bearer scheme
/// is replaced by <see cref="TestAuthHandler"/>.
/// </summary>
public class MyBookingApiWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Deterministic signing key used by the test host. The production application deliberately
    /// refuses to start without a strong JWT key (see the fail-fast validation in Program.cs); this
    /// value only satisfies that startup check.
    /// </summary>
    public const string TestSigningKey = "integration-tests-only-signing-key-0123456789-abcdef";

    // A unique database name per factory instance keeps each test fully isolated.
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Hermetic configuration: in the Development environment ASP.NET Core automatically loads
        // machine-local User Secrets, which means a developer's personal overrides (for example a
        // different Jwt:Audience or an enabled AdminBootstrap) would silently leak into the suite.
        // Pin the values the tests assert on so the results are identical on every machine and CI agent.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "MyBookingApi",
                ["Jwt:Audience"] = "MyBookingApiClient",
                ["Jwt:Key"] = TestSigningKey,
                // The first-run bootstrap is opt-in by design; tests that exercise it enable it explicitly.
                ["AdminBootstrap:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove every registration AddInfrastructure made for the AppDbContext. Critically, this
            // includes the EF Core "options configuration" entry that would otherwise re-apply UseSqlServer,
            // which would leave two database providers registered (SQL Server + InMemory) and fail at runtime.
            RemoveDbContextRegistrations(services);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            // Replace the default JWT bearer scheme with the deterministic test scheme.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Removes the <see cref="AppDbContext"/> registrations contributed by the production
    /// <c>AddInfrastructure</c> extension so a test-specific provider can be configured instead.
    /// </summary>
    private static void RemoveDbContextRegistrations(IServiceCollection services)
    {
        var descriptorsToRemove = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(AppDbContext)
                || descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || descriptor.ServiceType == typeof(DbContextOptions)
                // IDbContextOptionsConfiguration<TContext> holds the provider-configuring action; it is an
                // EF Core internal type so it is matched by name rather than a compile-time reference.
                || descriptor.ServiceType.Name == "IDbContextOptionsConfiguration`1")
            .ToList();

        foreach (var descriptor in descriptorsToRemove)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>
    /// Seeds data into the in-memory database used by the test host.
    /// </summary>
    /// <param name="seedAction">Action that populates the context; changes are persisted automatically.</param>
    public void Seed(Action<AppDbContext> seedAction)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seedAction(context);
        context.SaveChanges();
    }
}
