using System.Text;
using System.Text.Json;
using Harness.Core;
using Harness.Core.Observability;
using Harness.Host.Options;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public sealed record RunArtifactsSession(
    string RunId,
    string TracePath,
    string SummaryPath,
    string ScenarioName,
    int Epoch);

public class RunArtifactsWriter(
    IObservabilityEventSink eventSink,
    IOptions<HarnessOptions> options,
    ILogger<RunArtifactsWriter> logger)
    : IProvideLogger
{
    private readonly HarnessOptions _options = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ILogger Logger => logger;

    [ObserveBoundary]
    public RunArtifactsSession StartRun(int epoch)
    {
        var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
        Directory.CreateDirectory(artifactRoot);

        // ReSharper disable once StringLiteralTypo
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var scenarioName = SanitizePathSegment(_options.ScenarioName);
        var runId = $"{timestamp}-{scenarioName}-epoch{epoch:D2}";
        var runDirectory = Path.Combine(artifactRoot, runId);
        Directory.CreateDirectory(runDirectory);

        return new RunArtifactsSession(
            runId,
            Path.Combine(runDirectory, "trace.jsonl"),
            Path.Combine(runDirectory, "summary.md"),
            _options.ScenarioName,
            epoch);
    }

    [ObserveBoundary]
    public async Task WriteArtifactsAsync(
        RunArtifactsSession session,
        int totalSteps,
        string stopReason,
        StepResult? lastResult,
        CancellationToken cancellationToken)
    {
        var events = eventSink.GetEvents(session.RunId);
        try
        {
            await WriteTraceAsync(session, events, cancellationToken);
            await WriteSummaryAsync(session, totalSteps, stopReason, lastResult, events, cancellationToken);
        }
        finally
        {
            eventSink.Clear(session.RunId);
        }
    }

    private async Task WriteTraceAsync(
        RunArtifactsSession session,
        IReadOnlyList<HarnessEvent> events,
        CancellationToken cancellationToken)
    {
        var lines = events.Select(entry => JsonSerializer.Serialize(new
        {
            timestampUtc = entry.TimestampUtc,
            entry.Name,
            level = entry.Level.ToString(),
            entry.Message,
            runId = entry.Context.RunId,
            epoch = entry.Context.Epoch,
            step = entry.Context.Step,
            correlationId = entry.Context.CorrelationId,
            operation = entry.Context.Operation,
            data = entry.Data
        }, _jsonOptions));

        await File.WriteAllLinesAsync(session.TracePath, lines, cancellationToken);
    }

    private async Task WriteSummaryAsync(
        RunArtifactsSession session,
        int totalSteps,
        string stopReason,
        StepResult? lastResult,
        IReadOnlyList<HarnessEvent> events,
        CancellationToken cancellationToken)
    {
        var finalBuffers = lastResult?.BufferStatesAfter ?? [];
        var finalBuffersJson = JsonSerializer.Serialize(finalBuffers, new JsonSerializerOptions(_jsonOptions)
        {
            WriteIndented = true
        });

        var groupedEvents = events
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"- `{group.Key}`: {group.Count()}")
            .ToList();

        var sb = new StringBuilder();

        sb.Append($"""
                   # Run Summary

                   Scenario: `{session.ScenarioName}`
                   Run ID: `{session.RunId}`
                   Epoch: `{session.Epoch}`
                   Finished (UTC): `{DateTimeOffset.UtcNow:O}`
                   Total steps: `{totalSteps}`
                   Stop reason: `{stopReason}`
                   Recorded events: `{events.Count}`

                   """);

        if (lastResult is not null)
        {
            sb.Append($"""
                       Last selected rule: `{lastResult.SelectedRuleId ?? "<none>"}`
                       Matched rules: `{string.Join(", ", lastResult.SatisfiedRuleIds)}`
                       Operation count: `{lastResult.Operations.Count}`
                       Changed buffers: `{lastResult.BufferChanges.Count}`

                       """);
        }

        var errorSection = BuildErrorSection(lastResult, events);
        if (errorSection is not null)
        {
            sb.Append(errorSection);
            sb.AppendLine();
        }

        if (groupedEvents.Count > 0)
        {
            sb.AppendLine("## Event Counts");
            sb.AppendLine(string.Join(Environment.NewLine, groupedEvents));
            sb.AppendLine();
        }

        sb.Append($"""
                   ## Final Buffers
                   ```json
                   {finalBuffersJson}
                   ```

                   """);

        await File.WriteAllTextAsync(session.SummaryPath, sb.ToString(), cancellationToken);
    }

    private static string? BuildErrorSection(StepResult? lastResult, IReadOnlyList<HarnessEvent> events)
    {
        if (lastResult is not null && !string.IsNullOrWhiteSpace(lastResult.ErrorMessage))
        {
            return FormatError(
                lastResult.FailureStage,
                lastResult.ErrorType,
                lastResult.ErrorMessage,
                lastResult.ErrorDetails);
        }

        var failureEvent =
            events.LastOrDefault(entry => string.Equals(entry.Name, "step.failed", StringComparison.Ordinal));

        if (failureEvent?.Data is not { ValueKind: JsonValueKind.Object } element)
            return null;

        var failureStage = ReadStringProp(element, "failureStage");
        var errorType = ReadStringProp(element, "errorType");
        var errorMessage = ReadStringProp(element, "errorMessage");
        var errorSummary = ReadStringProp(element, "errorSummary");
        var errorDetails = ReadStringProp(element, "errorDetails");

        if (string.IsNullOrWhiteSpace(errorSummary) && string.IsNullOrWhiteSpace(errorDetails))
            return null;

        var combinedMessage = new List<string?>();
        if (!string.IsNullOrWhiteSpace(errorMessage)) combinedMessage.Add(errorMessage);
        if (!string.IsNullOrWhiteSpace(errorSummary)) combinedMessage.Add(errorSummary);

        return FormatError(
            failureStage,
            errorType,
            string.Join(" | ", combinedMessage),
            errorDetails);
    }

    private static string FormatError(
        string? failureStage,
        string? errorType,
        string? message,
        string? errorDetails)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Final Error");

        if (!string.IsNullOrWhiteSpace(failureStage))
            sb.AppendLine($"Failure stage: `{failureStage}`");
        if (!string.IsNullOrWhiteSpace(errorType))
            sb.AppendLine($"Error type: `{errorType}`");
        if (!string.IsNullOrWhiteSpace(message))
            sb.AppendLine($"Message: `{message}`");

        if (string.IsNullOrWhiteSpace(errorDetails)) return sb.ToString();

        sb.AppendLine("```text");
        sb.AppendLine(errorDetails);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string? ReadStringProp(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }
}