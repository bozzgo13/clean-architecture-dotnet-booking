using MyBookingApi.Domain.Common;
using MyBookingApi.Domain.Enums;

namespace MyBookingApi.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; } = UserRole.User;

    // EF Core needs a parameterless constructor
    protected User() { }

    public User(string email, string passwordHash, UserRole role = UserRole.User)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash));

        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    public void UpdateEmail(string newEmail)
    {
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new ArgumentException("Email cannot be empty.", nameof(newEmail));

        Email = newEmail;
    }

    public void ChangeRole(UserRole newRole)
    {
        Role = newRole;
    }
}