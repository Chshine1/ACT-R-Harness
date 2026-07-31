using System.Collections.Concurrent;
using Harness.Core.Observability;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Host;

public class StructuredObservabilitySink(ILogger<StructuredObservabilitySink> logger) : IObservabilityEventSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HarnessEvent>> _eventsByRunId = new();

    public void Record(
        string name,
        LogLevel level,
        string message,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        var context = HarnessExecutionContext.Current;
        var eventData = data ?? new Dictionary<string, object?>();
        var entry = new HarnessEvent(
            DateTimeOffset.UtcNow,
            name,
            level,
            message,
            new HarnessEventContext(
                context.RunId,
                context.Epoch,
                context.Step,
                context.CorrelationId,
                context.Operation),
            eventData);

        if (!string.IsNullOrWhiteSpace(context.RunId))
        {
            var queue = _eventsByRunId.GetOrAdd(context.RunId, static _ => new ConcurrentQueue<HarnessEvent>());
            queue.Enqueue(entry);
        }

        if (TryGetExceptionDetails(entry.Data, out var errorSummary, out var errorStackTrace))
        {
            logger.Log(
                level,
                "Event {EventName} runId={RunId} epoch={Epoch} step={Step} correlationId={CorrelationId} operation={Operation} error={ErrorSummary} data={EventData}{NewLine}{ErrorStackTrace}",
                entry.Name,
                entry.Context.RunId ?? "<none>",
                entry.Context.Epoch?.ToString() ?? "<none>",
                entry.Context.Step?.ToString() ?? "<none>",
                entry.Context.CorrelationId ?? "<none>",
                entry.Context.Operation ?? "<none>",
                errorSummary,
                ObservabilityFormatter.Summarize(entry.Data),
                Environment.NewLine,
                errorStackTrace);

            return;
        }

        logger.Log(
            level,
            "Event {EventName} runId={RunId} epoch={Epoch} step={Step} correlationId={CorrelationId} operation={Operation} data={EventData}",
            entry.Name,
            entry.Context.RunId ?? "<none>",
            entry.Context.Epoch?.ToString() ?? "<none>",
            entry.Context.Step?.ToString() ?? "<none>",
            entry.Context.CorrelationId ?? "<none>",
            entry.Context.Operation ?? "<none>",
            ObservabilityFormatter.Summarize(entry.Data));
    }

    private static bool TryGetExceptionDetails(
        IReadOnlyDictionary<string, object?> data,
        out string errorSummary,
        out string errorStackTrace)
    {
        errorSummary = data.TryGetValue("errorSummary", out var summaryValue)
            ? summaryValue?.ToString() ?? string.Empty
            : string.Empty;
        errorStackTrace = data.TryGetValue("errorStackTrace", out var stackTraceValue)
            ? stackTraceValue?.ToString() ?? string.Empty
            : string.Empty;

        return !string.IsNullOrWhiteSpace(errorSummary) || !string.IsNullOrWhiteSpace(errorStackTrace);
    }

    public IReadOnlyList<HarnessEvent> GetEvents(string runId)
    {
        return _eventsByRunId.TryGetValue(runId, out var queue)
            ? queue.ToArray()
            : [];
    }

    public void Clear(string runId)
    {
        _eventsByRunId.TryRemove(runId, out _);
    }
}
