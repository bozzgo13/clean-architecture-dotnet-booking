using FluentValidation.TestHelper;
using MyBookingApi.Application.Auth.Commands.Bootstrap;

/// <summary>
/// Unit tests for <see cref="BootstrapAdminUserCommandValidator"/>, verifying that an obviously invalid
/// bootstrap payload fails fast before any database work happens, while a well-formed one passes.
/// </summary>
public class BootstrapAdminUserCommandValidatorTests
{
    private readonly BootstrapAdminUserCommandValidator _validator = new();

    private static BootstrapAdminUserCommand ValidCommand() =>
        new("initial.admin@email.com", "Abcdef1!ghij", "Initial", "Administrator");

    [Fact]
    public void Should_Not_Have_Error_When_Command_Is_Valid()
    {
        // Act
        var result = _validator.TestValidate(ValidCommand());

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]            // missing
    [InlineData("not-an-email")] // invalid format
    [InlineData("email.com")]   // invalid format
    public void Should_Have_Error_When_Email_Is_Invalid(string email)
    {
        var command = ValidCommand() with { Email = email };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_Have_Error_When_Email_Exceeds_Maximum_Length()
    {
        var command = ValidCommand() with { Email = new string('a', 250) + "@example.com" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Have_Error_When_Password_Is_Empty(string password)
    {
        var command = ValidCommand() with { Password = password };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Have_Error_When_Password_Exceeds_Maximum_Length()
    {
        // Arrange: an oversized input must be rejected early so it cannot be used to exhaust resources
        // during hashing.
        var command = ValidCommand() with { Password = new string('a', BootstrapAdminUserCommandValidator.MaximumPasswordLength + 1) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Have_Error_When_FirstName_Is_Empty(string firstName)
    {
        var command = ValidCommand() with { FirstName = firstName };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Should_Have_Error_When_FirstName_Exceeds_Maximum_Length()
    {
        var command = ValidCommand() with { FirstName = new string('a', BootstrapAdminUserCommandValidator.MaximumNameLength + 1) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Have_Error_When_LastName_Is_Empty(string lastName)
    {
        var command = ValidCommand() with { LastName = lastName };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void Should_Have_Error_When_LastName_Exceeds_Maximum_Length()
    {
        var command = ValidCommand() with { LastName = new string('a', BootstrapAdminUserCommandValidator.MaximumNameLength + 1) };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }
}
