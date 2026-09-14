using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskFlow.Core.DTOs;
using Xunit;
using Xunit.Sdk;

namespace TaskFlow.TestInfrastructure.Http;

/// <summary>
/// Assertions over HTTP responses that report enough context to diagnose a failure
/// without re-running the test.
/// </summary>
/// <remarks>
/// <c>Assert.Equal(HttpStatusCode.OK, response.StatusCode)</c> fails with
/// "Expected OK, Actual InternalServerError" and nothing else. These helpers attach
/// the response body and, where the caller supplies it, the recorded traffic and the
/// application log.
/// </remarks>
public static class ApiAssert
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Asserts the response status, including the response body on failure.</summary>
    /// <param name="response">Response under assertion.</param>
    /// <param name="expected">Expected status code.</param>
    /// <param name="diagnostics">Optional extra context, such as recorded traffic and logs.</param>
    public static async Task StatusAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? diagnostics = null)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await ReadBodySafelyAsync(response);
        var request = response.RequestMessage;

        throw new XunitException(
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}." +
            $"{Environment.NewLine}Request : {request?.Method.Method} {request?.RequestUri}" +
            $"{Environment.NewLine}Response: {HttpExchangeRecorder.Redact(body)}" +
            (diagnostics is null ? string.Empty : Environment.NewLine + diagnostics));
    }

    /// <summary>
    /// Asserts the status and deserialises the body, failing with context if either step fails.
    /// </summary>
    public static async Task<T> StatusAndContentAsync<T>(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? diagnostics = null)
    {
        await StatusAsync(response, expected, diagnostics);

        var body = await ReadBodySafelyAsync(response);
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new XunitException(
                       $"Response body deserialised to null for {typeof(T).Name}." +
                       $"{Environment.NewLine}Body: {HttpExchangeRecorder.Redact(body)}");
        }
        catch (JsonException ex)
        {
            throw new XunitException(
                $"Could not deserialise the response into {typeof(T).Name}: {ex.Message}" +
                $"{Environment.NewLine}Body: {HttpExchangeRecorder.Redact(body)}" +
                (diagnostics is null ? string.Empty : Environment.NewLine + diagnostics));
        }
    }

    /// <summary>
    /// Asserts that the response is an ASP.NET Core model-validation problem
    /// mentioning <paramref name="expectedField"/>.
    /// </summary>
    /// <remarks>
    /// Model validation failures are produced by the framework as
    /// <c>ValidationProblemDetails</c>, not by the application's own
    /// <see cref="ErrorResponseDto"/>, so they are asserted separately.
    /// </remarks>
    public static async Task ValidationErrorForAsync(
        HttpResponseMessage response,
        string expectedField,
        string? diagnostics = null)
    {
        await StatusAsync(response, HttpStatusCode.BadRequest, diagnostics);

        var body = await ReadBodySafelyAsync(response);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("errors", out var errors))
        {
            throw new XunitException(
                $"Expected a validation problem containing an 'errors' object." +
                $"{Environment.NewLine}Body: {HttpExchangeRecorder.Redact(body)}");
        }

        var fields = errors.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.True(
            fields.Any(f => string.Equals(f, expectedField, StringComparison.OrdinalIgnoreCase)),
            $"Expected a validation error for '{expectedField}' but the response reported: " +
            $"{string.Join(", ", fields)}.{Environment.NewLine}Body: {HttpExchangeRecorder.Redact(body)}");
    }

    /// <summary>Reads the application's own error payload.</summary>
    public static async Task<ErrorResponseDto> ErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? diagnostics = null) =>
        await StatusAndContentAsync<ErrorResponseDto>(response, expected, diagnostics);

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"(could not read response body: {ex.Message})";
        }
    }
}
