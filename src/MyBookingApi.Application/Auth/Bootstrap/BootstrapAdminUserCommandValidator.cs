using FluentValidation;

namespace MyBookingApi.Application.Auth.Commands.Bootstrap;

/// <summary>
/// Validates the structural contract of the <see cref="BootstrapAdminUserCommand"/> payload so an
/// obviously invalid request fails fast before any database work happens.
///
/// It deliberately does NOT re-apply the interactive strong-password policy used by registration: the
/// credential is machine-generated and guaranteed to satisfy that policy by construction, so enforcing
/// it again here would only introduce a source of intermittent failure.
/// </summary>
public class BootstrapAdminUserCommandValidator : AbstractValidator<BootstrapAdminUserCommand>
{
    /// <summary>Upper bound on the email length, matching the storage constraint.</summary>
    public const int MaximumEmailLength = 256;

    /// <summary>Upper bound on the password length (resource-exhaustion defense for the hashing step).</summary>
    public const int MaximumPasswordLength = 128;

    /// <summary>Upper bound on the name lengths.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>
    /// Initializes the validation rules.
    /// </summary>
    public BootstrapAdminUserCommandValidator()
    {
        RuleFor(v => v.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(MaximumEmailLength)
            .WithMessage($"Email must not exceed {MaximumEmailLength} characters.");

        RuleFor(v => v.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(MaximumPasswordLength)
            .WithMessage($"Password must not exceed {MaximumPasswordLength} characters.");

        RuleFor(v => v.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(MaximumNameLength)
            .WithMessage($"First name must not exceed {MaximumNameLength} characters.");

        RuleFor(v => v.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(MaximumNameLength)
            .WithMessage($"Last name must not exceed {MaximumNameLength} characters.");
    }
}
