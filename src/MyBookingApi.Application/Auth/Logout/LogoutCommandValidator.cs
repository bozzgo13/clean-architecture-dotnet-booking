using FluentValidation;

namespace MyBookingApi.Application.Auth.Commands.Logout;

/// <summary>
/// Defines validation rules for the <see cref="LogoutCommand"/> payload. It enforces only that a token
/// is present and within a sane size bound, so that an obviously invalid request fails fast with a
/// 400 Bad Request before any database query is executed.
///
/// The token value is intentionally never matched against a format here: a well-formed but unknown
/// token must still receive the same successful response as a valid one, so this validator never leaks
/// whether a token has a plausible shape.
/// </summary>
public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    /// <summary>
    /// Upper bound on the refresh token length. A genuine token is 128 hexadecimal characters, so the
    /// limit is generous while still rejecting oversized input to protect the hashing step against
    /// resource-exhaustion attempts (OWASP API4:2023 - Unrestricted Resource Consumption).
    /// </summary>
    public const int MaximumRefreshTokenLength = 512;

    public LogoutCommandValidator()
    {
        RuleFor(v => v.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.")
            .MaximumLength(MaximumRefreshTokenLength)
            .WithMessage($"Refresh token must not exceed {MaximumRefreshTokenLength} characters.");
    }
}
