using Microsoft.AspNetCore.Identity;
using MyBookingApi.Application.Common.Interfaces;

namespace MyBookingApi.Infrastructure.Security;

/// <summary>
/// Password hashing implementation backed by ASP.NET Core Identity's password hasher.
/// It uses PBKDF2 with a per-hash random salt and a configurable iteration count, and prefixes
/// the result with a format marker so the algorithm can be upgraded over time.
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    // The user instance is not read by the default hashing algorithm, so a lightweight
    // placeholder type is used to satisfy the generic hasher without referencing the domain entity.
    private readonly PasswordHasher<object> _passwordHasher = new();

    /// <summary>
    /// A well-formed hash that never corresponds to a real credential. It is generated once per
    /// process and used to equalize the work done when no account matches, so that the time taken to
    /// reject a login does not reveal whether the email address is registered.
    /// </summary>
    private readonly string _nonExistentAccountHash;

    public IdentityPasswordHasher()
    {
        _nonExistentAccountHash = _passwordHasher.HashPassword(new object(), Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(new object(), password);
    }

    /// <inheritdoc />
    public bool VerifyPassword(string? hashedPassword, string providedPassword)
    {
        // An absent hash means "no such account". A dummy verification is still executed so that both
        // paths consume a comparable amount of CPU; the caller always gets false in that case.
        var hashToVerify = string.IsNullOrEmpty(hashedPassword) ? _nonExistentAccountHash : hashedPassword;

        var result = _passwordHasher.VerifyHashedPassword(new object(), hashToVerify, providedPassword);

        if (string.IsNullOrEmpty(hashedPassword))
        {
            return false;
        }

        // A successful verification may still ask for a rehash when the stored hash was produced with
        // an older algorithm or iteration count; both outcomes are a valid match.
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
