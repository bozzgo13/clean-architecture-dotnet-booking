using FluentValidation;

namespace MyBookingApi.Application.Auth.Commands.Login;

/// <summary>
/// Defines validation rules for the <see cref="LoginUserCommand"/> payload. It enforces only that the
/// credentials are present and reasonably bounded, so that an obviously invalid request fails fast
/// with a 400 Bad Request before any database query is executed.
///
/// The registration-time password policy is deliberately NOT re-applied here: a login request must not
/// reveal password composition rules, and accounts may legitimately hold credentials created under an
/// older policy.
/// </summary>
public class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    /// <summary>
    /// Upper bound on the password length. Login only needs to compare, but an oversized input is still
    /// rejected early to protect the password-verification step against resource-exhaustion attempts.
    /// </summary>
    public const int MaximumPasswordLength = 128;

    public LoginUserCommandValidator()
    {
        RuleFor(v => v.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(v => v.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(MaximumPasswordLength)
            .WithMessage($"Password must not exceed {MaximumPasswordLength} characters.");
    }
}
