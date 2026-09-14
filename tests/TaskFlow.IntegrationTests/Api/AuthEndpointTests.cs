using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Core.DTOs;
using TaskFlow.TestInfrastructure.Fixtures;
using TaskFlow.TestInfrastructure.Http;
using TaskFlow.TestInfrastructure.TestData;

namespace TaskFlow.IntegrationTests.Api;

/// <summary>
/// HTTP behaviour of <c>/api/Auth</c>, exercised through the real pipeline against
/// a real SQL Server.
/// </summary>
public class AuthEndpointTests : IntegrationTestBase
{
    public AuthEndpointTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    // ------------------------------------------------------------- register

    [Fact]
    public async Task Register_creates_the_account_and_returns_201_with_a_token()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", NewRegistration());

        var body = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            response, HttpStatusCode.Created, Diagnostics());

        Assert.Equal("new.user@taskflow.test", body.Email);
        Assert.Equal("New", body.FirstName);
        Assert.Equal("User", body.LastName);
        Assert.Equal(TestRoles.User, body.Role);
        Assert.True(body.UserId > 0);
        Assert.NotEmpty(body.Token);
    }

    [Fact]
    public async Task Register_persists_the_user_with_a_hashed_password()
    {
        await Client.PostAsJsonAsync("/api/auth/register", NewRegistration());

        var user = await QueryAsync(db =>
            db.Users.SingleAsync(u => u.Email == "new.user@taskflow.test"));

        Assert.Equal(TestRoles.User, user.Role);
        Assert.NotEqual(TestUsers.DefaultPassword, user.PasswordHash);
        Assert.NotEmpty(user.PasswordHash);
    }

    /// <summary>
    /// The unique index on Email must surface as a 409-style domain error, not a
    /// database exception.
    /// </summary>
    [Fact]
    public async Task Register_rejects_an_email_that_is_already_taken()
    {
        await SeedUserAsync(TestUsers.Alice);

        var response = await Client.PostAsJsonAsync("/api/auth/register",
            NewRegistration(email: TestUsers.Alice.Email));

        var error = await ApiAssert.ErrorAsync(response, HttpStatusCode.BadRequest, Diagnostics());
        Assert.Equal("Email already exists", error.Message);

        // The duplicate must not have been written.
        var count = await QueryAsync(db => db.Users.CountAsync(u => u.Email == TestUsers.Alice.Email));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("", "Last", "valid@taskflow.test", "Passw0rd!", "FirstName")]
    [InlineData("First", "", "valid@taskflow.test", "Passw0rd!", "LastName")]
    [InlineData("First", "Last", "not-an-email", "Passw0rd!", "Email")]
    [InlineData("First", "Last", "valid@taskflow.test", "12345", "Password")]
    public async Task Register_rejects_an_invalid_payload(
        string firstName, string lastName, string email, string password, string expectedField)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Password = password,
            ConfirmPassword = password
        });

        await ApiAssert.ValidationErrorForAsync(response, expectedField, Diagnostics());
    }

    [Fact]
    public async Task Register_rejects_a_mismatched_password_confirmation()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            FirstName = "First",
            LastName = "Last",
            Email = "mismatch@taskflow.test",
            Password = "Passw0rd!",
            ConfirmPassword = "Different!"
        });

        await ApiAssert.ValidationErrorForAsync(response, "ConfirmPassword", Diagnostics());

        var exists = await QueryAsync(db => db.Users.AnyAsync(u => u.Email == "mismatch@taskflow.test"));
        Assert.False(exists);
    }

    [Fact]
    public async Task Register_does_not_let_the_caller_choose_a_role()
    {
        // RegisterDto has no Role member, so this sends one that the model binder must
        // ignore. Self-registration must never be a path to elevated privilege.
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Sneaky",
            lastName = "User",
            email = "sneaky@taskflow.test",
            password = TestUsers.DefaultPassword,
            confirmPassword = TestUsers.DefaultPassword,
            role = TestRoles.Admin
        });

        await ApiAssert.StatusAsync(response, HttpStatusCode.Created, Diagnostics());

        var user = await QueryAsync(db => db.Users.SingleAsync(u => u.Email == "sneaky@taskflow.test"));
        Assert.Equal(TestRoles.User, user.Role);
    }

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task Login_returns_a_token_and_the_users_profile()
    {
        await SeedUserAsync(TestUsers.Alice);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = TestUsers.Alice.Email,
            Password = TestUsers.Alice.Password
        });

        var body = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            response, HttpStatusCode.OK, Diagnostics());

        Assert.Equal(TestUsers.Alice.Email, body.Email);
        Assert.Equal(TestUsers.Alice.FirstName, body.FirstName);
        Assert.Equal(TestRoles.User, body.Role);
        Assert.NotEmpty(body.Token);
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password_with_401()
    {
        await SeedUserAsync(TestUsers.Alice);

        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = TestUsers.Alice.Email,
            Password = "definitely-not-the-password"
        });

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    [Fact]
    public async Task Login_rejects_an_unknown_account_with_401()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "nobody@taskflow.test",
            Password = TestUsers.DefaultPassword
        });

        await ApiAssert.StatusAsync(response, HttpStatusCode.Unauthorized, Diagnostics());
    }

    /// <summary>
    /// A wrong password and an unknown account must be indistinguishable over HTTP.
    /// </summary>
    /// <remarks>
    /// Asserted end to end as well as in the unit tests, because the response the
    /// client sees is what an attacker would use to enumerate accounts, and that
    /// depends on the controller as much as on the service.
    /// </remarks>
    [Fact]
    public async Task Login_responses_do_not_reveal_whether_an_account_exists()
    {
        await SeedUserAsync(TestUsers.Alice);

        // The password must satisfy the DTO's MinLength(6), otherwise both requests
        // fail model validation with a 400 and never reach the authentication path
        // this test is about.
        const string wellFormedButWrong = "wrong-password";

        var wrongPassword = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = TestUsers.Alice.Email,
            Password = wellFormedButWrong
        });

        var unknownAccount = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "nobody@taskflow.test",
            Password = wellFormedButWrong
        });

        await ApiAssert.StatusAsync(wrongPassword, HttpStatusCode.Unauthorized, Diagnostics());
        await ApiAssert.StatusAsync(unknownAccount, HttpStatusCode.Unauthorized, Diagnostics());

        var first = await wrongPassword.Content.ReadAsStringAsync();
        var second = await unknownAccount.Content.ReadAsStringAsync();
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("not-an-email", "Passw0rd!", "Email")]
    [InlineData("valid@taskflow.test", "12345", "Password")]
    public async Task Login_rejects_an_invalid_payload(string email, string password, string expectedField)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = password });

        await ApiAssert.ValidationErrorForAsync(response, expectedField, Diagnostics());
    }

    [Fact]
    public async Task The_issued_token_is_accepted_by_protected_endpoints()
    {
        await SeedUserAsync(TestUsers.Alice);

        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = TestUsers.Alice.Email,
            Password = TestUsers.Alice.Password
        });

        var auth = await ApiAssert.StatusAndContentAsync<AuthResponseDto>(
            login, HttpStatusCode.OK, Diagnostics());

        using var authenticated = Api.CreateApiClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await authenticated.GetAsync("/api/projects");

        await ApiAssert.StatusAsync(response, HttpStatusCode.OK, Diagnostics());
    }

    private static RegisterDto NewRegistration(string email = "new.user@taskflow.test") => new()
    {
        FirstName = "New",
        LastName = "User",
        Email = email,
        Password = TestUsers.DefaultPassword,
        ConfirmPassword = TestUsers.DefaultPassword
    };
}
