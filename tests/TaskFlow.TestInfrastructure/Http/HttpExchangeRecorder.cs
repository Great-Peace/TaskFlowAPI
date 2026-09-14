using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskFlow.TestInfrastructure.Http;

/// <summary>
/// One recorded request/response pair.
/// </summary>
public sealed record HttpExchange(
    string Method,
    string Uri,
    string? RequestBody,
    int StatusCode,
    string? ResponseBody,
    TimeSpan Duration);

/// <summary>
/// Records HTTP traffic between the tests and the application so a failure can show
/// the exact exchange that produced it.
/// </summary>
/// <remarks>
/// Bodies and headers are redacted before being stored. A test log is an artifact
/// that lands in CI output, so it must never contain a bearer token or a password,
/// even one belonging to a throwaway test user.
/// </remarks>
public sealed class HttpExchangeRecorder
{
    private const string Redacted = "***REDACTED***";

    /// <summary>Maximum body length retained, to keep failure output readable.</summary>
    private const int MaxBodyLength = 2000;

    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// JSON properties whose values must never be printed.
    /// Matches "token": "..." and friends, including escaped content.
    /// </summary>
    private static readonly Regex SensitiveJsonProperty = new(
        "\"(token|password|confirmPassword|currentPassword|newPassword|confirmNewPassword|passwordHash|secretKey)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"",
        Options);

    private readonly ConcurrentQueue<HttpExchange> _exchanges = new();

    /// <summary>Exchanges recorded since the last <see cref="Clear"/>.</summary>
    public IReadOnlyCollection<HttpExchange> Exchanges => _exchanges.ToArray();

    /// <summary>Adds an exchange to the recording.</summary>
    /// <remarks>
    /// Normally called by <see cref="RecordingHttpHandler"/>. Public so that the
    /// recorder's own behaviour - redaction and rendering - can be tested without
    /// standing up an HTTP pipeline to produce traffic.
    /// </remarks>
    public void Record(HttpExchange exchange) => _exchanges.Enqueue(exchange);

    /// <summary>Discards recorded traffic so output is attributable to a single test.</summary>
    public void Clear()
    {
        while (_exchanges.TryDequeue(out _))
        {
            // Intentionally empty: draining the queue is the whole operation.
        }
    }

    /// <summary>Replaces the values of sensitive JSON properties with a placeholder.</summary>
    public static string? Redact(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var redacted = SensitiveJsonProperty.Replace(
            body,
            match => $"\"{match.Groups[1].Value}\":\"{Redacted}\"");

        return redacted.Length > MaxBodyLength
            ? redacted[..MaxBodyLength] + $"... (truncated, {redacted.Length} chars total)"
            : redacted;
    }

    /// <summary>Renders the recorded traffic as text for inclusion in a failure message.</summary>
    public string Render()
    {
        var exchanges = _exchanges.ToArray();
        if (exchanges.Length == 0)
        {
            return "(no HTTP exchanges recorded)";
        }

        var builder = new StringBuilder();
        foreach (var exchange in exchanges)
        {
            builder.Append("--> ")
                   .Append(exchange.Method)
                   .Append(' ')
                   .AppendLine(exchange.Uri);

            if (!string.IsNullOrWhiteSpace(exchange.RequestBody))
            {
                builder.Append("    body: ").AppendLine(exchange.RequestBody);
            }

            builder.Append("<-- ")
                   .Append(exchange.StatusCode)
                   .Append(" (")
                   .Append(exchange.Duration.TotalMilliseconds.ToString("F0"))
                   .AppendLine("ms)");

            if (!string.IsNullOrWhiteSpace(exchange.ResponseBody))
            {
                builder.Append("    body: ").AppendLine(exchange.ResponseBody);
            }
        }

        return builder.ToString();
    }
}
