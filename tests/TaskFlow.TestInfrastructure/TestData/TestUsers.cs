namespace TaskFlow.TestInfrastructure.TestData;

/// <summary>
/// The fixed cast of identities used across the test suites.
/// </summary>
public static class TestUsers
{
    /// <summary>
    /// Password shared by the built-in identities. Satisfies the API's
    /// <c>MinLength(6)</c> rule on registration and login.
    /// </summary>
    public const string DefaultPassword = "Test-Passw0rd!";

    /// <summary>Ordinary user. The "owner" in ownership tests.</summary>
    public static readonly TestUser Alice =
        new("Alice", "Anderson", "alice@taskflow.test", DefaultPassword, TestRoles.User);

    /// <summary>Ordinary user. The "other user" in authorization-boundary tests.</summary>
    public static readonly TestUser Bob =
        new("Bob", "Brown", "bob@taskflow.test", DefaultPassword, TestRoles.User);

    /// <summary>
    /// Administrator. Must be seeded directly, because the register endpoint
    /// hardcodes the <see cref="TestRoles.User"/> role.
    /// </summary>
    public static readonly TestUser Admin =
        new("Ada", "Admin", "admin@taskflow.test", DefaultPassword, TestRoles.Admin);
}
