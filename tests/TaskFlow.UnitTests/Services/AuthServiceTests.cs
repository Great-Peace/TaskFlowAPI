using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Moq;
using TaskFlow.Core.Configuration;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;
using TaskFlow.Core.Services;

namespace TaskFlow.UnitTests.Services;

/// <summary>
/// Behaviour of registration, login and token issuance.
/// </summary>
/// <remarks>
/// <para>
/// Only the persistence boundary is mocked. The real <see cref="Pbkdf2PasswordHasher"/>
/// is used because it is a pure function with no I/O: substituting it would replace
/// the behaviour under test ("the stored value verifies, and is not the password")
/// with an assertion that a mock was called.
/// </para>
/// <para>
/// The clock is a <see cref="FakeTimeProvider"/> fixed to a known instant, so token
/// expiry is an exact assertion rather than a tolerance window.
/// </para>
/// </remarks>
public class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private const string SigningKey = "unit-test-signing-key-at-least-32-characters-long";

    /// <summary>
    /// The message a rejected login must produce.
    /// </summary>
    /// <remarks>
    /// Pinned because mutation testing found that emptying this message killed no
    /// test: the suite asserted the exception type, and that both failure paths
    /// produced the same message, but never that the message said anything at all.
    /// A blank rejection reason would have shipped unnoticed.
    /// </remarks>
    private const string ExpectedRejectionMessage = "Invalid email or password";

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Pbkdf2PasswordHasher _hasher = new();

    public AuthServiceTests()
    {
        _unitOfWork.SetupGet(u => u.Users).Returns(_users.Object);
    }

    private AuthService CreateService(JwtSettings? settings = null) => new(
        _unitOfWork.Object,
        Options.Create(settings ?? new JwtSettings
        {
            SecretKey = SigningKey,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiryInHours = 24
        }),
        _hasher,
        _clock);

    private User ExistingUser(string email = "someone@taskflow.test", string password = "Passw0rd!") => new()
    {
        Id = 42,
        FirstName = "Existing",
        LastName = "User",
        Email = email,
        PasswordHash = _hasher.Hash(password),
        Role = "User",
        CreatedAt = Now.UtcDateTime
    };

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task LoginAsync_returns_the_users_profile_and_a_token()
    {
        var user = ExistingUser();
        _users.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var result = await CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = "Passw0rd!" });

        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.FirstName, result.FirstName);
        Assert.Equal(user.LastName, result.LastName);
        Assert.Equal(user.Role, result.Role);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task LoginAsync_rejects_an_unknown_email()
    {
        _users.Setup(r => r.GetByEmailAsync("nobody@taskflow.test")).ReturnsAsync((User?)null);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService().LoginAsync(new LoginDto
            {
                Email = "nobody@taskflow.test",
                Password = "Passw0rd!"
            }));

        Assert.Equal(ExpectedRejectionMessage, exception.Message);
    }

    [Fact]
    public async Task LoginAsync_rejects_an_incorrect_password()
    {
        var user = ExistingUser();
        _users.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService().LoginAsync(new LoginDto
            {
                Email = user.Email,
                Password = "not the right password"
            }));

        Assert.Equal(ExpectedRejectionMessage, exception.Message);
    }

    /// <summary>
    /// An unknown account and a wrong password must be indistinguishable to the caller.
    /// </summary>
    /// <remarks>
    /// Distinct messages would let an attacker enumerate which e-mail addresses are
    /// registered. This asserts the security property directly rather than trusting
    /// that the two code paths happen to share a literal.
    /// </remarks>
    [Fact]
    public async Task LoginAsync_does_not_reveal_whether_the_account_exists()
    {
        var user = ExistingUser();
        _users.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _users.Setup(r => r.GetByEmailAsync("nobody@taskflow.test")).ReturnsAsync((User?)null);

        var service = CreateService();

        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginDto { Email = user.Email, Password = "wrong" }));

        var unknownAccount = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginDto { Email = "nobody@taskflow.test", Password = "wrong" }));

        Assert.Equal(wrongPassword.Message, unknownAccount.Message);
        Assert.Equal(ExpectedRejectionMessage, wrongPassword.Message);
    }

    [Fact]
    public async Task LoginAsync_does_not_write_to_the_database()
    {
        var user = ExistingUser();
        _users.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        await CreateService().LoginAsync(new LoginDto { Email = user.Email, Password = "Passw0rd!" });

        // Strict mocks make this assertion real: an unconfigured SaveChangesAsync call
        // would have thrown rather than silently passing.
        _unitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    // ------------------------------------------------------------- register

    [Fact]
    public async Task RegisterAsync_rejects_an_email_that_is_already_registered()
    {
        _users.Setup(r => r.EmailExistsAsync("taken@taskflow.test")).ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().RegisterAsync(NewRegistration("taken@taskflow.test")));

        Assert.Equal("Email already exists", exception.Message);
        _unitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_stores_a_hash_rather_than_the_password()
    {
        User? saved = null;
        _users.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<User>()))
              .Callback<User>(u => saved = u)
              .ReturnsAsync((User u) => u);
        _unitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        await CreateService().RegisterAsync(NewRegistration(password: "PlaintextPassw0rd!"));

        Assert.NotNull(saved);
        Assert.NotEqual("PlaintextPassw0rd!", saved!.PasswordHash);
        Assert.True(_hasher.Verify("PlaintextPassw0rd!", saved.PasswordHash));
    }

    [Fact]
    public async Task RegisterAsync_always_assigns_the_ordinary_user_role()
    {
        User? saved = null;
        _users.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<User>()))
              .Callback<User>(u => saved = u)
              .ReturnsAsync((User u) => u);
        _unitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await CreateService().RegisterAsync(NewRegistration());

        // Self-registration must never be a route to elevated privilege.
        Assert.Equal("User", saved!.Role);
        Assert.Equal("User", result.Role);
    }

    [Fact]
    public async Task RegisterAsync_stamps_the_creation_time_from_the_clock()
    {
        User? saved = null;
        _users.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<User>()))
              .Callback<User>(u => saved = u)
              .ReturnsAsync((User u) => u);
        _unitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        await CreateService().RegisterAsync(NewRegistration());

        Assert.Equal(Now.UtcDateTime, saved!.CreatedAt);
    }

    [Fact]
    public async Task RegisterAsync_persists_before_issuing_a_token()
    {
        var sequence = new List<string>();
        _users.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<User>()))
              .Callback<User>(_ => sequence.Add("add"))
              .ReturnsAsync((User u) => u);
        _unitOfWork.Setup(u => u.SaveChangesAsync())
                   .Callback(() => sequence.Add("save"))
                   .ReturnsAsync(1);

        // The returned token embeds the user's Id, which only exists after the insert.
        var result = await CreateService().RegisterAsync(NewRegistration());

        Assert.Equal(new[] { "add", "save" }, sequence);
        Assert.NotEmpty(result.Token);
    }

    // ---------------------------------------------------------------- token

    [Fact]
    public void GenerateJwtToken_includes_the_claims_the_api_authorises_on()
    {
        var user = ExistingUser();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(CreateService().GenerateJwtToken(user));

        Assert.Equal(user.Id.ToString(), ClaimValue(token, ClaimTypes.NameIdentifier));
        Assert.Equal(user.Email, ClaimValue(token, ClaimTypes.Email));
        Assert.Equal("Existing User", ClaimValue(token, ClaimTypes.Name));
        Assert.Equal("User", ClaimValue(token, ClaimTypes.Role));
    }

    [Fact]
    public void GenerateJwtToken_uses_the_configured_issuer_and_audience()
    {
        var token = new JwtSecurityTokenHandler()
            .ReadJwtToken(CreateService().GenerateJwtToken(ExistingUser()));

        Assert.Equal("TestIssuer", token.Issuer);
        Assert.Contains("TestAudience", token.Audiences);
    }

    /// <summary>
    /// Expiry must come from configuration.
    /// </summary>
    /// <remarks>
    /// <c>JwtSettings:ExpiryInHours</c> was present in configuration but ignored: the
    /// service hardcoded a 24-hour expiry. An operator changing the setting would have
    /// seen no effect, which is worse than not offering the setting at all.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(72)]
    public void GenerateJwtToken_honours_the_configured_expiry(int expiryInHours)
    {
        var service = CreateService(new JwtSettings
        {
            SecretKey = SigningKey,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiryInHours = expiryInHours
        });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateJwtToken(ExistingUser()));

        // JWT exp has one-second resolution, so compare at that resolution.
        var expected = Now.UtcDateTime.AddHours(expiryInHours);
        Assert.Equal(expected, token.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GenerateJwtToken_produces_a_token_that_validates_against_the_configured_key()
    {
        var token = CreateService().GenerateJwtToken(ExistingUser());

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "TestIssuer",
            ValidAudience = "TestAudience",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),

            // Validate the signature, not the lifetime: the fake clock is in the past
            // relative to the validating handler's system clock.
            ValidateLifetime = false
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);

        Assert.Equal("42", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void GenerateJwtToken_rejects_a_token_signed_with_a_different_key()
    {
        var token = CreateService().GenerateJwtToken(ExistingUser());

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("a-completely-different-key-of-sufficient-length"))
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
    }

    private static RegisterDto NewRegistration(
        string email = "new.user@taskflow.test",
        string password = "Passw0rd!") => new()
        {
            FirstName = "New",
            LastName = "User",
            Email = email,
            Password = password,
            ConfirmPassword = password
        };

    private static string? ClaimValue(JwtSecurityToken token, string claimType) =>
        token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
}
