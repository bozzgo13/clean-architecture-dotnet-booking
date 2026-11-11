using FluentValidation;

namespace MyBookingApi.Application.Bookings.Commands.CreateBooking;

public class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(v => v.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(v => v.ResourceId)
            .NotEmpty().WithMessage("ResourceId is required.");

        RuleFor(v => v.StartTime)
            .GreaterThan(DateTime.UtcNow).WithMessage("Start time must be in the future.");

        RuleFor(v => v.EndTime)
            .GreaterThan(v => v.StartTime).WithMessage("End time must be greater than start time.");
    }
}