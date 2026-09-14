namespace TaskFlow.TestInfrastructure.TestData;

/// <summary>
/// Role names used by the application's authorization policies.
/// </summary>
/// <remarks>
/// Mirrors the literals used in <c>AuthService</c> and the <c>[Authorize(Roles = ...)]</c>
/// attributes. Centralised so a role rename fails to compile rather than silently
/// disabling an authorization test.
/// </remarks>
public static class TestRoles
{
    /// <summary>Default role assigned to every self-registered account.</summary>
    public const string User = "User";

    /// <summary>Elevated role; cannot be obtained through the public register endpoint.</summary>
    public const string Admin = "Admin";
}
