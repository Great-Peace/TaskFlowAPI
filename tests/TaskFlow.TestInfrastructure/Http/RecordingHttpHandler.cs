using System.Diagnostics;

namespace TaskFlow.TestInfrastructure.Http;

/// <summary>
/// Delegating handler that feeds every request/response through an
/// <see cref="HttpExchangeRecorder"/>.
/// </summary>
/// <remarks>
/// Sits on the test's <see cref="HttpClient"/> rather than inside the application,
/// so it observes exactly what the test sent and what the API returned, and adds
/// nothing to the production pipeline.
/// </remarks>
public sealed class RecordingHttpHandler : DelegatingHandler
{
    private readonly HttpExchangeRecorder _recorder;

    /// <summary>Creates the handler.</summary>
    /// <param name="recorder">Collector that the exchanges are written to.</param>
    public RecordingHttpHandler(HttpExchangeRecorder recorder) => _recorder = recorder;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        stopwatch.Stop();

        // Buffer the response so reading it here does not consume the stream the test needs.
        await response.Content.LoadIntoBufferAsync();
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        _recorder.Record(new HttpExchange(
            request.Method.Method,
            request.RequestUri?.ToString() ?? "(no uri)",
            HttpExchangeRecorder.Redact(requestBody),
            (int)response.StatusCode,
            HttpExchangeRecorder.Redact(responseBody),
            stopwatch.Elapsed));

        return response;
    }
}
