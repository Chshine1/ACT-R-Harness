using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Harness.Core.Observability;

public sealed record HarnessEventContext(
    string? RunId,
    int? Epoch,
    int? Step,
    string? CorrelationId,
    string? Operation);

public sealed record HarnessEvent(
    DateTimeOffset TimestampUtc,
    string Name,
    LogLevel Level,
    string Message,
    HarnessEventContext Context,
    JsonElement? Data);

public interface IObservabilityEventSink
{
    void Record(
        string name,
        LogLevel level,
        string message,
        object? data = null);

    IReadOnlyList<HarnessEvent> GetEvents(string runId);

    void Clear(string runId);
}
