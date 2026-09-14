using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaskFlow.Core.Configuration;

namespace TaskFlow.UnitTests.Configuration;

/// <summary>
/// Validation of the JWT configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Added in response to mutation testing. Replacing the default values of
/// <c>SecretKey</c>, <c>Issuer</c> and <c>Audience</c> killed no test, which
/// revealed that nothing verified this validation at all - despite
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> having been introduced
/// specifically so that a missing or too-short signing key stops the application
/// at startup rather than failing on the first login.
/// </para>
/// <para>
/// These go through the real options pipeline rather than calling
/// <c>Validator.TryValidateObject</c> directly, so they prove the binding and
/// validation are actually wired up, not merely that the attributes are present.
/// </para>
/// </remarks>
public class JwtSettingsValidationTests
{
    private const string ValidKey = "a-signing-key-that-is-at-least-32-characters-long";

    /// <summary>
    /// Binds a configuration section through the same pipeline the application
    /// uses and returns the validated settings, throwing if validation fails.
    /// </summary>
    private static JwtSettings BindAndValidate(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        services.AddOptions<JwtSettings>()
            .Bind(services.BuildServiceProvider()
                          .GetRequiredService<IConfiguration>()
                          .GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations();

        return services.BuildServiceProvider()
                       .GetRequiredService<IOptions<JwtSettings>>()
                       .Value;
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["JwtSettings:SecretKey"] = ValidKey,
        ["JwtSettings:Issuer"] = "TaskFlowAPI",
        ["JwtSettings:Audience"] = "TaskFlowClients",
        ["JwtSettings:ExpiryInHours"] = "24"
    };

    [Fact]
    public void A_complete_configuration_binds_successfully()
    {
        var settings = BindAndValidate(ValidConfiguration());

        Assert.Equal(ValidKey, settings.SecretKey);
        Assert.Equal("TaskFlowAPI", settings.Issuer);
        Assert.Equal("TaskFlowClients", settings.Audience);
        Assert.Equal(24, settings.ExpiryInHours);
    }

    /// <summary>
    /// A signing key shorter than 256 bits must be refused.
    /// </summary>
    /// <remarks>
    /// HMAC-SHA256 needs at least 256 bits of key material. A shorter key is a
    /// configuration error, and catching it at startup is the whole reason the
    /// validation exists.
    /// </remarks>
    [Theory]
    [InlineData("short")]
    [InlineData("still-far-too-short-for-hs256")]
    [InlineData("exactly-31-characters-long-abc")]
    public void A_signing_key_below_the_minimum_length_is_rejected(string weakKey)
    {
        var configuration = ValidConfiguration();
        configuration["JwtSettings:SecretKey"] = weakKey;

        var exception = Assert.Throws<OptionsValidationException>(() => BindAndValidate(configuration));

        Assert.Contains("at least 32 characters", string.Join(" ", exception.Failures));
    }

    [Fact]
    public void A_signing_key_at_exactly_the_minimum_length_is_accepted()
    {
        var configuration = ValidConfiguration();
        var boundaryKey = new string('k', 32);
        configuration["JwtSettings:SecretKey"] = boundaryKey;

        var settings = BindAndValidate(configuration);

        Assert.Equal(boundaryKey, settings.SecretKey);
    }

    [Theory]
    [InlineData("JwtSettings:SecretKey")]
    [InlineData("JwtSettings:Issuer")]
    [InlineData("JwtSettings:Audience")]
    public void A_missing_required_setting_is_rejected(string missingKey)
    {
        var configuration = ValidConfiguration();
        configuration.Remove(missingKey);

        var exception = Assert.Throws<OptionsValidationException>(() => BindAndValidate(configuration));

        // Assert what the operator is told, not merely that something failed. An
        // omitted setting must be reported as missing; being told instead that the
        // value is the wrong length sends them looking for the wrong problem.
        //
        // Mutation testing found this: changing the SecretKey default from
        // string.Empty to a short non-empty literal left the type-only assertion
        // passing, because MinLength then failed in place of Required.
        var field = missingKey.Split(':').Last();
        Assert.Contains($"The {field} field is required", string.Join(" ", exception.Failures));
    }

    [Theory]
    [InlineData("JwtSettings:SecretKey")]
    [InlineData("JwtSettings:Issuer")]
    [InlineData("JwtSettings:Audience")]
    public void An_empty_required_setting_is_rejected(string emptyKey)
    {
        // [Required(AllowEmptyStrings = false)] - an empty string in configuration is
        // as much a misconfiguration as an absent one, and is the more likely mistake.
        var configuration = ValidConfiguration();
        configuration[emptyKey] = string.Empty;

        Assert.Throws<OptionsValidationException>(() => BindAndValidate(configuration));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    public void An_expiry_outside_the_permitted_range_is_rejected(int hours)
    {
        var configuration = ValidConfiguration();
        configuration["JwtSettings:ExpiryInHours"] = hours.ToString();

        Assert.Throws<OptionsValidationException>(() => BindAndValidate(configuration));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(72)]
    [InlineData(720)]
    public void An_expiry_within_the_permitted_range_is_accepted(int hours)
    {
        var configuration = ValidConfiguration();
        configuration["JwtSettings:ExpiryInHours"] = hours.ToString();

        Assert.Equal(hours, BindAndValidate(configuration).ExpiryInHours);
    }

    /// <summary>
    /// Omitting the expiry falls back to the documented default.
    /// </summary>
    /// <remarks>
    /// The default is 24 hours because that is what the service hardcoded before the
    /// setting was honoured. Omitting the value must therefore preserve the previous
    /// behaviour rather than change it.
    /// </remarks>
    [Fact]
    public void An_omitted_expiry_defaults_to_twenty_four_hours()
    {
        var configuration = ValidConfiguration();
        configuration.Remove("JwtSettings:ExpiryInHours");

        Assert.Equal(24, BindAndValidate(configuration).ExpiryInHours);
    }

    /// <summary>
    /// An entirely absent section must fail rather than silently yield blank settings.
    /// </summary>
    [Fact]
    public void A_missing_configuration_section_is_rejected()
    {
        Assert.Throws<OptionsValidationException>(() =>
            BindAndValidate(new Dictionary<string, string?>()));
    }
}
