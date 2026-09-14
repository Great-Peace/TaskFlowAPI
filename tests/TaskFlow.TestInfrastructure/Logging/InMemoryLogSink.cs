using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace TaskFlow.TestInfrastructure.Logging;

/// <summary>
/// Captures the application's Serilog output in memory so a failing test can print
/// what the server actually did.
/// </summary>
/// <remarks>
/// An integration test failure that reports only "expected 200 but got 500" forces
/// the developer to re-run locally with a debugger. Attaching the application log to
/// the failure usually identifies the cause immediately.
/// </remarks>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    /// <summary>Events captured since the last <see cref="Clear"/>.</summary>
    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>Discards captured events. Called between tests so logs are attributable to one test.</summary>
    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
            // Intentionally empty: draining the queue is the whole operation.
        }
    }

    /// <summary>
    /// Renders captured events at or above <paramref name="minimumLevel"/> as text.
    /// </summary>
    /// <param name="minimumLevel">Lowest level to include.</param>
    /// <param name="maxEvents">Cap on the number of events, newest kept, to keep failure output readable.</param>
    public string Render(LogEventLevel minimumLevel = LogEventLevel.Information, int maxEvents = 100)
    {
        var selected = _events
            .Where(e => e.Level >= minimumLevel)
            .TakeLast(maxEvents)
            .ToArray();

        if (selected.Length == 0)
        {
            return "(no application log events captured)";
        }

        var builder = new StringBuilder();
        foreach (var logEvent in selected)
        {
            builder.Append('[')
                   .Append(logEvent.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                   .Append(' ')
                   .Append(logEvent.Level.ToString().ToUpperInvariant())
                   .Append("] ")
                   .AppendLine(logEvent.RenderMessage(CultureInfo.InvariantCulture));

            if (logEvent.Exception is not null)
            {
                builder.AppendLine(logEvent.Exception.ToString());
            }
        }

        return builder.ToString();
    }
}
