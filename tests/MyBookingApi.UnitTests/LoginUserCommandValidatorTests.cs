using FluentValidation.TestHelper;
using MyBookingApi.Application.Auth.Commands.Login;

/// <summary>
/// Unit tests for <see cref="LoginUserCommandValidator"/>, verifying that malformed or empty login
/// payloads are rejected before any database work happens, while valid credentials pass untouched.
/// </summary>
public class LoginUserCommandValidatorTests
{
    private readonly LoginUserCommandValidator _validator = new();

    private static LoginUserCommand ValidCommand() =>
        new("user@email.com", "Str0ng!Pass");

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
    [InlineData("@email.com")]  // invalid format
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

    [Fact]
    public void Should_Have_Error_When_Password_Is_Empty()
    {
        var command = ValidCommand() with { Password = "" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Have_Error_When_Password_Exceeds_Maximum_Length()
    {
        // Arrange: an oversized input must be rejected early so it cannot be used to exhaust resources
        // during password verification.
        var command = ValidCommand() with { Password = new string('a', LoginUserCommandValidator.MaximumPasswordLength + 1) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("abc")]      // far below the registration minimum length
    [InlineData("alllower")]  // no complexity at all
    public void Should_Not_Apply_Registration_Password_Policy_On_Login(string password)
    {
        // Arrange: the login endpoint must not re-enforce registration-time password complexity, so
        // accounts created under an older policy can still authenticate.
        var command = ValidCommand() with { Password = password };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }
}
