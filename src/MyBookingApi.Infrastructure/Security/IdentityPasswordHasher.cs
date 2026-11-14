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

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(new object(), password);
    }
}
