using FluentValidation.TestHelper;
using MyBookingApi.Application.Bookings.Commands.CreateBooking;

public class CreateBookingCommandValidatorTests
{
    private readonly CreateBookingCommandValidator _validator;

    public CreateBookingCommandValidatorTests()
    {
        _validator = new CreateBookingCommandValidator();
    }

    [Fact]
    public void Should_Have_Error_When_ResourceId_Is_Empty()
    {
        // Arrange: Create a command with an empty ResourceId, 
        // but ensure UserId and dates are valid so they don't trigger unexpected validation errors.
        var command = new CreateBookingCommand(
            ResourceId: Guid.Empty, // Invalid (Empty)
            UserId: Guid.NewGuid(),     // Valid
            StartTime: DateTime.UtcNow.AddDays(1), // Valid future date
            EndTime: DateTime.UtcNow.AddDays(1).AddHours(2) // Valid end time
        );

        // Act
        var result = _validator.TestValidate(command);

        // Assert: Verify that validation fails specifically for ResourceId
        result.ShouldHaveValidationErrorFor(x => x.ResourceId);
    }

    [Fact]
    public void Should_Have_Error_When_StartTime_Is_In_The_Past()
    {
        // Arrange
        var command = new CreateBookingCommand(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.StartTime);
    }
}