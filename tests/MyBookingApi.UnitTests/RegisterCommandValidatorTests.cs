using FluentValidation.TestHelper;
using MyBookingApi.Application.Auth.Commands.Register;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand ValidCommand() =>
        new("pero.klepec@yahoo.com", "P3t3rK!3p3c", "Peter", "Klepec");

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
    [InlineData("@email")]      // invalid format
    [InlineData("email.com")]   // invalid format
    public void Should_Have_Error_When_Email_Is_Invalid(string email)
    {
        var command = ValidCommand() with { Email = email };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("Ab1!")]           // fewer than 8 characters
    [InlineData("lowercase1!")]    // no uppercase letter
    [InlineData("UPPERCASE1!")]    // no lowercase letter
    [InlineData("NoDigit!")]       // no digit
    [InlineData("NoSpecCh1")]      // no special character
    public void Should_Have_Error_When_Password_Is_Weak(string password)
    {
        var command = ValidCommand() with { Password = password };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
    [Theory]
    [InlineData("Abcdef1!")]                      // simple as it gets, but still meets the minimum requirements
    [InlineData("Ab    1!")]                      // with empty spaces
    [InlineData("XM2@hU+-AynV?xLb)^Ui4zj,2t!")]   // complex and long password
    public void Should_Not_Have_Error_When_Password_Meets_All_Policy_Rules(string password)
    {
        var command = ValidCommand() with { Password = password };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Should_Have_Error_When_FirstName_Is_Empty()
    {
        var command = ValidCommand() with { FirstName = "" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Should_Have_Error_When_LastName_Is_Empty()
    {
        var command = ValidCommand() with { LastName = "   " };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }
}
