namespace TaskFlow.TestInfrastructure.TestData;

/// <summary>
/// A deterministic test identity: fixed name, e-mail, password and role.
/// </summary>
/// <remarks>
/// Tests refer to identities by name (<c>TestUsers.Alice</c>) rather than inventing
/// literals, so that an assertion about "another user's project" is unambiguous.
/// These credentials are test-only fixtures and never appear in any environment
/// the application actually runs in.
/// </remarks>
public sealed record TestUser(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Role)
{
    /// <summary>Name as the API renders it in <c>ProjectDto.CreatedByName</c>.</summary>
    public string FullName => $"{FirstName} {LastName}";
}
