using FluentValidation;

namespace MyBookingApi.Application.Auth.Commands.Register;

/// <summary>
/// Defines validation rules for the <see cref="RegisterCommand"/> payload, enforcing the
/// registration contract (email format, strong password policy and required name fields)
/// before any business logic executes.
/// </summary>
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>
    /// Password policy: minimum length plus at least one lowercase letter, one uppercase letter,
    /// one digit and one special character.
    /// </summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// Upper bound on the password length, protecting the password-hashing step against
    /// resource-exhaustion attempts using extremely large inputs.
    /// </summary>
    public const int MaximumPasswordLength = 128;

    // Character class covering the allowed special characters: !"#$%&'()*+,-./:;<=>?@[\]^_`{|}~
    private const string SpecialCharacterPattern = @"[!""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~]";

    public RegisterCommandValidator()
    {
        RuleFor(v => v.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(v => v.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(MinimumPasswordLength)
            .WithMessage($"Password must be at least {MinimumPasswordLength} characters long.")
            // Cap the length so an oversized payload cannot be used to exhaust CPU/memory while hashing.
            .MaximumLength(MaximumPasswordLength)
            .WithMessage($"Password must not exceed {MaximumPasswordLength} characters.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches(SpecialCharacterPattern)
            .WithMessage(@"Password must contain at least one special character (!""#$%&'()*+,-./:;<=>?@[\]^_`{|}~).");

        RuleFor(v => v.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(v => v.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");
    }
}
