using FluentValidation;

namespace MyBookingApi.Application.Resources.Commands.CreateResource;

/// <summary>
/// Defines validation rules for the <see cref="CreateResourceCommand"/> payload to ensure data
/// integrity before any business logic or database access is executed (fail-fast boundary).
/// </summary>
public class CreateResourceCommandValidator : AbstractValidator<CreateResourceCommand>
{
    /// <summary>
    /// Maximum accepted length of the resource name. Mirrors the database column bound
    /// (<c>ResourceConfiguration</c>) so oversized input is rejected before it reaches the database
    /// (OWASP API4:2023 - Unrestricted Resource Consumption).
    /// </summary>
    public const int MaximumNameLength = 150;

    /// <summary>
    /// Maximum accepted length of the resource description. Mirrors the database column bound.
    /// </summary>
    public const int MaximumDescriptionLength = 500;

    public CreateResourceCommandValidator()
    {
        RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(MaximumNameLength)
            .WithMessage($"Name must not exceed {MaximumNameLength} characters.");

        // The description is optional; only its upper bound is enforced so the payload cannot be
        // used to exceed the persisted column size.
        RuleFor(v => v.Description)
            .MaximumLength(MaximumDescriptionLength)
            .WithMessage($"Description must not exceed {MaximumDescriptionLength} characters.");
    }
}
