using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Harness.Shared.Observability;

public sealed record FailureReport(
    [property: JsonPropertyName("boundary")] string Boundary,
    [property: JsonPropertyName("epoch")] int? Epoch,
    [property: JsonPropertyName("step")] int? Step,
    [property: JsonPropertyName("trace_id")] string? TraceId,
    [property: JsonPropertyName("span_id")] string? SpanId,
    [property: JsonPropertyName("exception_type")] string ExceptionType,
    [property: JsonPropertyName("exception_message")] string ExceptionMessage,
    [property: JsonPropertyName("exception_stacktrace")] string ExceptionStacktrace)
{
    public static FailureReport FromException(Exception exception, string boundary)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        var activity = Activity.Current;
        var attachedBoundary = exception.Data[TracingModel.Tags.ObservabilityBoundary] as string;
        return new(
            string.IsNullOrWhiteSpace(attachedBoundary) ? boundary : attachedBoundary,
            Epoch: null,
            Step: null,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString());
    }
}

public sealed record SuccessfulStepReport(
    [property: JsonPropertyName("epoch")] int Epoch,
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("rule_id")] string? RuleId,
    [property: JsonPropertyName("stop_reason")] string StopReason);

public sealed record RunReport(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("trace_id")] string? TraceId,
    [property: JsonPropertyName("span_id")] string? SpanId,
    [property: JsonPropertyName("started_at_utc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTimeOffset CompletedAtUtc,
    [property: JsonPropertyName("last_successful_step")]
    SuccessfulStepReport? LastSuccessfulStep,
    [property: JsonPropertyName("failure")] FailureReport? Failure);
