using TaskFlow.TestInfrastructure.Http;

namespace TaskFlow.UnitTests.Diagnostics;

/// <summary>
/// Redaction of secrets in captured diagnostics.
/// </summary>
/// <remarks>
/// Failure diagnostics are written to CI logs, which are far more widely readable
/// than the application's own logs and are retained as build artifacts. A bearer
/// token or password that reaches them has effectively been published. These tests
/// exist because that guarantee is only worth having if it is enforced.
/// </remarks>
public class RedactionTests
{
    [Fact]
    public void A_bearer_token_in_a_response_body_is_redacted()
    {
        const string body =
            """{"token":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.abc123","userId":1}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", redacted);
        Assert.Contains("REDACTED", redacted);

        // Non-sensitive fields must survive, or the diagnostics stop being useful.
        Assert.Contains("\"userId\":1", redacted);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("confirmPassword")]
    [InlineData("currentPassword")]
    [InlineData("newPassword")]
    [InlineData("confirmNewPassword")]
    [InlineData("passwordHash")]
    [InlineData("secretKey")]
    [InlineData("token")]
    public void Every_sensitive_field_is_redacted(string fieldName)
    {
        var body = $$"""{"{{fieldName}}":"super-secret-value","email":"user@taskflow.test"}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.DoesNotContain("super-secret-value", redacted);
        Assert.Contains("user@taskflow.test", redacted);
    }

    [Fact]
    public void Redaction_is_case_insensitive()
    {
        // Serializer settings, hand-written payloads and different clients disagree
        // about casing; the guarantee must not depend on which one produced the body.
        const string body = """{"Password":"secret-one","TOKEN":"secret-two"}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.DoesNotContain("secret-one", redacted);
        Assert.DoesNotContain("secret-two", redacted);
    }

    [Fact]
    public void Escaped_characters_inside_a_secret_do_not_defeat_redaction()
    {
        // A JSON string containing an escaped quote must not terminate the match
        // early and leave the remainder of the secret visible.
        const string body = """{"password":"aw\"kward\\value","email":"user@taskflow.test"}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.DoesNotContain("kward", redacted);
        Assert.Contains("user@taskflow.test", redacted);
    }

    /// <summary>
    /// A very long secret is removed in full, not merely trimmed.
    /// </summary>
    /// <remarks>
    /// Redaction runs before truncation, which is the order that matters: a 5000
    /// character token collapses to the placeholder, so the body never reaches the
    /// length limit and no part of the secret can survive in the retained prefix.
    /// Truncating first would leave the opening of the token in the log.
    /// </remarks>
    [Fact]
    public void A_very_long_secret_is_removed_entirely()
    {
        var body = $$"""{"token":"{{new string('x', 5000)}}","note":"tail"}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.NotNull(redacted);
        Assert.DoesNotContain("xxxxxxxxxx", redacted);
        Assert.Contains("REDACTED", redacted!);
        Assert.Contains("\"note\":\"tail\"", redacted!);
    }

    /// <summary>A long body with no secrets is truncated so failure output stays readable.</summary>
    [Fact]
    public void A_long_body_without_secrets_is_truncated()
    {
        var body = $$"""{"description":"{{new string('a', 5000)}}"}""";

        var redacted = HttpExchangeRecorder.Redact(body);

        Assert.NotNull(redacted);
        Assert.Contains("truncated", redacted!);
        Assert.True(redacted!.Length < 2200, $"Expected a truncated body, got {redacted.Length} characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_body_is_handled(string? body)
    {
        Assert.Equal(body, HttpExchangeRecorder.Redact(body));
    }

    [Fact]
    public void A_body_with_no_secrets_is_left_alone()
    {
        const string body = """{"name":"Apollo","status":"Active"}""";

        Assert.Equal(body, HttpExchangeRecorder.Redact(body));
    }

    [Fact]
    public void Recorded_exchanges_are_redacted_when_rendered()
    {
        var recorder = new HttpExchangeRecorder();
        recorder.Record(new HttpExchange(
            "POST",
            "http://localhost/api/auth/login",
            HttpExchangeRecorder.Redact("""{"email":"a@b.test","password":"hunter2"}"""),
            200,
            HttpExchangeRecorder.Redact("""{"token":"eyJhbGciOi.secret.sig"}"""),
            TimeSpan.FromMilliseconds(12)));

        var rendered = recorder.Render();

        Assert.DoesNotContain("hunter2", rendered);
        Assert.DoesNotContain("eyJhbGciOi", rendered);
        Assert.Contains("POST", rendered);
        Assert.Contains("200", rendered);
    }

    [Fact]
    public void Clearing_the_recorder_discards_previous_traffic()
    {
        // Diagnostics must belong to the failing test, not to whatever ran before it.
        var recorder = new HttpExchangeRecorder();
        recorder.Record(new HttpExchange("GET", "/api/projects", null, 200, null, TimeSpan.Zero));

        recorder.Clear();

        Assert.Empty(recorder.Exchanges);
        Assert.Contains("no HTTP exchanges recorded", recorder.Render());
    }
}
