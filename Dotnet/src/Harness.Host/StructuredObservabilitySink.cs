using System.Collections.Concurrent;
using Harness.Abstractions.Observability;
using Harness.Core.Observability;
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
