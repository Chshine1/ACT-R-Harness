using System.Collections.Concurrent;
using System.Text.Json;
using Harness.Core.Observability;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Harness.Host;

public class StructuredObservabilitySink(ILogger<StructuredObservabilitySink> logger) : IObservabilityEventSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HarnessEvent>> _eventsByRunId = new();

    private static readonly JsonSerializerOptions ConvertOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = false
    };

    public void Record(string name, LogLevel level, string message, object? data = null)
    {
        JsonElement? jsonData = data is not null
            ? JsonSerializer.SerializeToElement(data, ConvertOptions)
            : null;

        var context = HarnessExecutionContext.Current;
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
            jsonData);

        if (!string.IsNullOrWhiteSpace(context.RunId))
        {
            var queue = _eventsByRunId.GetOrAdd(context.RunId, _ => new ConcurrentQueue<HarnessEvent>());
            queue.Enqueue(entry);
        }

        if (TryGetExceptionDetails(jsonData, out var errorSummary, out var errorStackTrace))
        {
            logger.Log(level,
                "Event {EventName} runId={RunId} epoch={Epoch} step={Step} correlationId={CorrelationId} operation={Operation} error={ErrorSummary} data={EventData}{NewLine}{ErrorStackTrace}",
                entry.Name,
                entry.Context.RunId ?? "<none>",
                entry.Context.Epoch?.ToString() ?? "<none>",
                entry.Context.Step?.ToString() ?? "<none>",
                entry.Context.CorrelationId ?? "<none>",
                entry.Context.Operation ?? "<none>",
                errorSummary,
                ObservabilityFormatter.Summarize(jsonData),
                Environment.NewLine,
                errorStackTrace);
            return;
        }

        logger.Log(level,
            "Event {EventName} runId={RunId} epoch={Epoch} step={Step} correlationId={CorrelationId} operation={Operation} data={EventData}",
            entry.Name,
            entry.Context.RunId ?? "<none>",
            entry.Context.Epoch?.ToString() ?? "<none>",
            entry.Context.Step?.ToString() ?? "<none>",
            entry.Context.CorrelationId ?? "<none>",
            entry.Context.Operation ?? "<none>",
            ObservabilityFormatter.Summarize(jsonData));
    }

    private static bool TryGetExceptionDetails(
        JsonElement? data,
        out string errorSummary,
        out string errorStackTrace)
    {
        errorSummary = string.Empty;
        errorStackTrace = string.Empty;

        if (data is not { ValueKind: JsonValueKind.Object } element)
            return false;

        if (element.TryGetProperty("errorSummary", out var summaryProp) &&
            summaryProp.ValueKind == JsonValueKind.String)
        {
            errorSummary = summaryProp.GetString() ?? "";
        }

        if (element.TryGetProperty("errorStackTrace", out var stackProp) &&
            stackProp.ValueKind == JsonValueKind.String)
        {
            errorStackTrace = stackProp.GetString() ?? "";
        }

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