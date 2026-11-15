using MediatR;
using Microsoft.Extensions.Options;
using MyBookingApi.Application.Auth.Commands.PruneRefreshTokens;

namespace MyBookingApi.WebApi.HostedServices;

/// <summary>
/// Background maintenance job that periodically deletes refresh tokens which expired or were revoked
/// long ago, so the token store cannot grow without bound (OWASP API4:2023 - Unrestricted Resource
/// Consumption). It delegates the actual pruning to the application layer through a MediatR command
/// and only owns the scheduling.
/// </summary>
public sealed class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RefreshTokenCleanupOptions _options;
    private readonly ILogger<RefreshTokenCleanupService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenCleanupService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create a per-run scope, because the database context and the MediatR sender are scoped services.</param>
    /// <param name="options">The validated cleanup configuration (interval and retention).</param>
    /// <param name="logger">The structured logger used to report cleanup runs.</param>
    public RefreshTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<RefreshTokenCleanupOptions> options,
        ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the cleanup on a fixed interval until the host is stopped. The first run is deferred by
    /// one full interval so application startup is never delayed by maintenance work.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host begins shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping; this is the expected way for the loop to end.
        }
    }

    /// <summary>
    /// Performs a single cleanup run in its own scope. Any failure is logged and swallowed so a
    /// transient database problem can never tear down the background loop.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            // Only tokens that expired or were revoked before this instant are eligible for deletion.
            var cutoffUtc = DateTime.UtcNow.AddDays(-_options.RetentionDays);

            await sender.Send(new PruneRefreshTokensCommand(cutoffUtc), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refresh-token cleanup run failed; it will be retried on the next interval.");
        }
    }
}
