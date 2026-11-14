using FluentValidation.TestHelper;
using MyBookingApi.Application.Bookings.Queries.GetBookings;

public class GetBookingsQueryValidatorTests
{
    private readonly GetBookingsQueryValidator _validator;

    public GetBookingsQueryValidatorTests()
    {
        _validator = new GetBookingsQueryValidator();
    }

    [Fact]
    public void Should_Have_Error_When_RequestingUserId_Is_Empty()
    {
        // Arrange
        var query = new GetBookingsQuery(Guid.Empty, IsAdministrator: false);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.RequestingUserId);
    }

    [Fact]
    public void Should_Not_Have_Error_When_RequestingUserId_Is_Provided()
    {
        // Arrange
        var query = new GetBookingsQuery(Guid.NewGuid(), IsAdministrator: true);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.RequestingUserId);
    }
}
