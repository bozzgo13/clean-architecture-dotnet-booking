using FluentValidation.TestHelper;
using MyBookingApi.Application.Auth.Commands.Refresh;

/// <summary>
/// Unit tests for <see cref="RefreshTokenCommandValidator"/>, verifying that an empty or oversized
/// refresh token is rejected before any database work happens, while a plausible token passes untouched.
/// </summary>
public class RefreshTokenCommandValidatorTests
{
    private readonly RefreshTokenCommandValidator _validator = new();

    private static RefreshTokenCommand ValidCommand() =>
        new(new string('A', 128));

    [Fact]
    public void Should_Not_Have_Error_When_Token_Is_Valid()
    {
        // Act
        var result = _validator.TestValidate(ValidCommand());

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Have_Error_When_Token_Is_Empty(string token)
    {
        var command = ValidCommand() with { RefreshToken = token };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }

    [Fact]
    public void Should_Have_Error_When_Token_Exceeds_Maximum_Length()
    {
        // Arrange: an oversized input must be rejected early so it cannot be used to exhaust resources
        // during hashing.
        var command = ValidCommand() with { RefreshToken = new string('A', RefreshTokenCommandValidator.MaximumRefreshTokenLength + 1) };

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }
}
