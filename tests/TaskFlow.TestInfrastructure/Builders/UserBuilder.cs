using TaskFlow.Core.Entities;
using TaskFlow.Core.Services;
using TaskFlow.Core.Services.Interface;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.TestInfrastructure.Builders;

/// <summary>
/// Builds <see cref="User"/> entities for seeding.
/// </summary>
/// <remarks>
/// Passwords are hashed with the application's own <see cref="IPasswordHasher"/>,
/// so a seeded user can authenticate through the real login endpoint. Re-implementing
/// the hash here would let the test format drift away from production.
/// </remarks>
public sealed class UserBuilder
{
    private static readonly IPasswordHasher Hasher = new Pbkdf2PasswordHasher();

    private string _firstName = "Test";
    private string _lastName = "User";
    private string _email = "test.user@taskflow.test";
    private string _password = TestUsers.DefaultPassword;
    private string _role = TestRoles.User;
    private DateTime _createdAt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Starts a builder pre-populated from a known <see cref="TestUser"/>.</summary>
    public static UserBuilder From(TestUser user) => new UserBuilder()
        .WithFirstName(user.FirstName)
        .WithLastName(user.LastName)
        .WithEmail(user.Email)
        .WithPassword(user.Password)
        .WithRole(user.Role);

    public UserBuilder WithFirstName(string firstName) { _firstName = firstName; return this; }

    public UserBuilder WithLastName(string lastName) { _lastName = lastName; return this; }

    public UserBuilder WithEmail(string email) { _email = email; return this; }

    /// <summary>Sets the plaintext password; it is hashed when <see cref="Build"/> is called.</summary>
    public UserBuilder WithPassword(string password) { _password = password; return this; }

    public UserBuilder WithRole(string role) { _role = role; return this; }

    public UserBuilder CreatedAt(DateTime createdAtUtc) { _createdAt = createdAtUtc; return this; }

    /// <summary>Materialises the entity. The Id is left at 0 for the database to assign.</summary>
    public User Build() => new()
    {
        FirstName = _firstName,
        LastName = _lastName,
        Email = _email,
        PasswordHash = Hasher.Hash(_password),
        Role = _role,
        CreatedAt = _createdAt
    };
}
