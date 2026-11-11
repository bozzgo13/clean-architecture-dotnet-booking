using FluentValidation;

namespace MyBookingApi.Application.Bookings.Commands.CreateBooking;

/// <summary>
/// Defines validation rules for the <see cref="CreateBookingCommand"/> payload 
/// to ensure data integrity before business logic execution.
/// </summary>
public class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(v => v.UserId)
            .NotEmpty().WithMessage("UserId is required.");
            
        RuleFor(v => v.ResourceId)
            .NotEmpty().WithMessage("ResourceId is required.");

        RuleFor(v => v.StartTime)
            .NotEmpty().WithMessage("Start time is required.")
            .GreaterThan(DateTime.UtcNow).WithMessage("Start time must be in the future.");

        RuleFor(v => v.EndTime)
            .NotEmpty().WithMessage("End time is required.")
            .GreaterThan(v => v.StartTime).WithMessage("End time must be greater than start time.");            
    }
}