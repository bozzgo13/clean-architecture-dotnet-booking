using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBookingApi.Domain.Enums;
using MyBookingApi.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// Integration tests for the first-run administrator bootstrap, verifying the end-to-end behaviour of
/// <c>AdminBootstrapHostedService</c> during host startup: it is inert by default and provisions exactly
/// one administrator when explicitly enabled through configuration.
/// </summary>
public class AdminBootstrapIntegrationTests
{
    /// <summary>
    /// With no <c>AdminBootstrap</c> configuration (the shipped default) the feature must be completely
    /// inert: starting the host provisions no account at all.
    /// </summary>
    [Fact]
    public async Task AdminBootstrap_WhenDisabledByDefault_DoesNotProvisionAnyUser()
    {
        // Arrange
        using var factory = new MyBookingApiWebApplicationFactory();

        // Act: creating the client starts the host and runs every hosted service.
        using var client = factory.CreateClient();

        // Assert
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await context.Users.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// When explicitly enabled with an email, the host must provision a single administrator whose
    /// credential is hashed (never stored in plaintext) and whose role is <see cref="UserRole.Admin"/>.
    /// </summary>
    [Fact]
    public async Task AdminBootstrap_WhenEnabled_ProvisionsAdministrator()
    {
        // Arrange: opt in only for this host, exactly as a first deployment would.
        using var factory = new MyBookingApiWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                // Override at the same configuration layer as the factory's hermetic defaults so this
                // value (appended last) takes precedence for this host only.
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AdminBootstrap:Enabled"] = "true",
                        ["AdminBootstrap:Email"] = "Bootstrapped.Admin@email.com",
                    });
                });
            });

        // Act
        using var client = factory.CreateClient();

        // Assert
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await context.Users.SingleAsync();
        admin.Email.Should().Be("bootstrapped.admin@email.com");
        admin.Role.Should().Be(UserRole.Admin);
        admin.PasswordHash.Should().NotBeNullOrWhiteSpace();
    }
}
