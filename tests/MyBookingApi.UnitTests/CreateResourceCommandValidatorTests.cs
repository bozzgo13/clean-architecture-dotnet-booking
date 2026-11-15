using FluentValidation.TestHelper;
using MyBookingApi.Application.Resources.Commands.CreateResource;

/// <summary>
/// Unit tests for <see cref="CreateResourceCommandValidator"/>, verifying that invalid payloads fail
/// fast (before any database access) while well-formed ones pass.
/// </summary>
public class CreateResourceCommandValidatorTests
{
    private readonly CreateResourceCommandValidator _validator = new();

    private static CreateResourceCommand ValidCommand() =>
        new("ConfRoom-A:1F", "First-floor conference room A with a projector", true);

    [Fact]
    public void Should_Not_Have_Error_When_Command_Is_Valid()
    {
        // Act
        var result = _validator.TestValidate(ValidCommand());

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Have_Error_When_Name_Is_Empty(string name)
    {
        var command = ValidCommand() with { Name = name };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_Name_Exceeds_Maximum_Length()
    {
        // Arrange: an oversized name must be rejected early so it cannot exceed the persisted column.
        var command = ValidCommand() with { Name = new string('a', CreateResourceCommandValidator.MaximumNameLength + 1) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Name_Is_At_Maximum_Length()
    {
        var command = ValidCommand() with { Name = new string('a', CreateResourceCommandValidator.MaximumNameLength) };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Description_Is_Null()
    {
        // The description is optional.
        var command = ValidCommand() with { Description = null };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Should_Have_Error_When_Description_Exceeds_Maximum_Length()
    {
        var command = ValidCommand() with { Description = new string('a', CreateResourceCommandValidator.MaximumDescriptionLength + 1) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }
}
