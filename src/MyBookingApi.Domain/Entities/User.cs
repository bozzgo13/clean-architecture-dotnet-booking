using MyBookingApi.Domain.Common;
using MyBookingApi.Domain.Enums;

namespace MyBookingApi.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; } = UserRole.User;

    // EF Core needs a parameterless constructor
    protected User() { }

    public User(string email, string passwordHash, string firstName, string lastName, UserRole role = UserRole.User)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash));

        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name cannot be empty.", nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name cannot be empty.", nameof(lastName));

        // Email is stored normalized so that uniqueness and lookups are case-insensitive.
        Email = NormalizeEmail(email);
        PasswordHash = passwordHash;
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Role = role;
    }

    /// <summary>
    /// Normalizes an email address (trims surrounding whitespace and lowercases it) so that
    /// <c>user@email.com</c> and <c>USER@EMAIL.COM</c> are treated as the same address.
    /// </summary>
    /// <param name="email">The raw email address to normalize.</param>
    /// <returns>The normalized email address.</returns>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void UpdateEmail(string newEmail)
    {
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new ArgumentException("Email cannot be empty.", nameof(newEmail));

        Email = NormalizeEmail(newEmail);
    }

    public void ChangeRole(UserRole newRole)
    {
        Role = newRole;
    }
}