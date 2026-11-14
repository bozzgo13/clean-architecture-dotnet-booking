using FluentValidation;

namespace MyBookingApi.Application.Bookings.Queries.GetBookings;

/// <summary>
/// Defines validation rules for the <see cref="GetBookingsQuery"/> payload
/// to ensure the caller identity is present before query execution.
/// </summary>
public class GetBookingsQueryValidator : AbstractValidator<GetBookingsQuery>
{
    public GetBookingsQueryValidator()
    {
        RuleFor(v => v.RequestingUserId)
            .NotEmpty().WithMessage("RequestingUserId is required.");
    }
}
