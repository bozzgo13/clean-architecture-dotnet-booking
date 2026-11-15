using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Options;
using MyBookingApi.Application.Auth.Commands.Bootstrap;

namespace MyBookingApi.WebApi.HostedServices;

/// <summary>
/// One-shot, opt-in provisioning of the first administrator, executed as part of host startup.
///
/// <see cref="IHostedService"/> (rather than <see cref="BackgroundService"/>) is used on purpose: this
/// is a single action, not a long-running loop, and the host awaits <see cref="StartAsync"/> so the
/// account exists before the application begins serving traffic. The whole path is inert unless
/// <c>AdminBootstrap:Enabled</c> is true, so it disappears from the running system once the account has
/// been created.
/// </summary>
public sealed class AdminBootstrapHostedService : IHostedService
{
    /// <summary>Length of the generated one-time password. Comfortably exceeds the application policy.</summary>
    private const int OneTimePasswordLength = 32;

    private const string LowerCaseAlphabet = "abcdefghijklmnopqrstuvwxyz";
    private const string UpperCaseAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitAlphabet = "0123456789";
    private const string SpecialAlphabet = "!@#$%^&*()-_=+";
    private const string AllAlphabet =
        LowerCaseAlphabet + UpperCaseAlphabet + DigitAlphabet + SpecialAlphabet;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AdminBootstrapOptions _options;
    private readonly ILogger<AdminBootstrapHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminBootstrapHostedService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create a scope, because the database context and the MediatR sender are scoped services.</param>
    /// <param name="options">The validated bootstrap configuration.</param>
    /// <param name="logger">The structured logger used to report the bootstrap outcome.</param>
    public AdminBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AdminBootstrapOptions> options,
        ILogger<AdminBootstrapHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Provisions the initial administrator when the feature is enabled. When it is disabled, or no
    /// email was configured, the method returns without touching the database.
    /// </summary>
    /// <param name="cancellationToken">Signalled when the host aborts startup.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email))
        {
            _logger.LogWarning(
                "Admin bootstrap is enabled but no email was configured; skipping provisioning.");

            return;
        }

        // The credential is generated here, in memory, and never read from configuration or source.
        var oneTimePassword = GenerateOneTimePassword();

        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(
            new BootstrapAdminUserCommand(
                _options.Email,
                oneTimePassword,
                _options.FirstName,
                _options.LastName),
            cancellationToken);

        if (result != AdminBootstrapResult.Created)
        {
            return;
        }

        // One-time delivery of the credential. Prefer stdout captured by the deploy job over a log
        // aggregator, and never persist the plaintext anywhere.
        Console.WriteLine($"[AdminBootstrap] Account: {_options.Email}");
        Console.WriteLine($"[AdminBootstrap] One-time password: {oneTimePassword}");
        Console.WriteLine(
            "[AdminBootstrap] Change this password on first sign-in, then unset AdminBootstrap__Enabled.");

        _logger.LogWarning(
            "Administrator bootstrap completed; remove the AdminBootstrap configuration once the " +
            "credential has been rotated. Email={Email}",
            _options.Email);
    }

    /// <summary>No resources are held, so there is nothing to release on shutdown.</summary>
    /// <param name="cancellationToken">Signalled when the host is stopping.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Produces a high-entropy password from the operating system's CSPRNG. The first characters are
    /// drawn from each required class so the credential is guaranteed to satisfy the application's
    /// strong-password policy, and the result is shuffled so those characters are not in fixed
    /// positions.
    /// </summary>
    /// <returns>A freshly generated one-time password.</returns>
    private static string GenerateOneTimePassword()
    {
        var characters = new char[OneTimePasswordLength];

        characters[0] = Pick(LowerCaseAlphabet);
        characters[1] = Pick(UpperCaseAlphabet);
        characters[2] = Pick(DigitAlphabet);
        characters[3] = Pick(SpecialAlphabet);

        for (var i = 4; i < characters.Length; i++)
        {
            characters[i] = Pick(AllAlphabet);
        }

        // Fisher-Yates shuffle using the CSPRNG, so the guaranteed characters are not predictable.
        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters);
    }

    /// <summary>Selects a uniformly random character from the supplied alphabet (no modulo bias).</summary>
    /// <param name="alphabet">The set of characters to draw from.</param>
    /// <returns>The selected character.</returns>
    private static char Pick(string alphabet)
        => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
