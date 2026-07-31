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
    string RunDirectory,
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

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var scenarioName = SanitizePathSegment(_options.Scenario.Name);
        var runId = $"{timestamp}-{scenarioName}-epoch{epoch:D2}";
        var runDirectory = Path.Combine(artifactRoot, runId);
        Directory.CreateDirectory(runDirectory);

        return new RunArtifactsSession(
            runId,
            runDirectory,
            Path.Combine(runDirectory, "trace.jsonl"),
            Path.Combine(runDirectory, "summary.md"),
            _options.Scenario.Name,
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

        var lines = new List<string>
        {
            "# Run Summary",
            string.Empty,
            $"Scenario: `{session.ScenarioName}`",
            $"Run ID: `{session.RunId}`",
            $"Epoch: `{session.Epoch}`",
            $"Finished (UTC): `{DateTimeOffset.UtcNow:O}`",
            $"Total steps: `{totalSteps}`",
            $"Stop reason: `{stopReason}`",
            $"Recorded events: `{events.Count}`"
        };

        if (lastResult is not null)
        {
            lines.Add($"Last selected rule: `{lastResult.SelectedRuleId ?? "<none>"}`");
            lines.Add($"Matched rules: `{string.Join(", ", lastResult.SatisfiedRuleIds)}`");
            lines.Add($"Operation count: `{lastResult.Operations.Count}`");
            lines.Add($"Changed buffers: `{lastResult.BufferChanges.Count}`");

            if (!string.IsNullOrWhiteSpace(lastResult.ErrorMessage))
            {
                lines.Add(string.Empty);
                lines.Add("## Final Error");
                lines.Add($"Failure stage: `{lastResult.FailureStage ?? "<unknown>"}`");
                lines.Add($"Error type: `{lastResult.ErrorType ?? "<unknown>"}`");
                lines.Add($"Message: `{lastResult.ErrorMessage}`");

                if (!string.IsNullOrWhiteSpace(lastResult.ErrorDetails))
                {
                    lines.Add("```text");
                    lines.Add(lastResult.ErrorDetails);
                    lines.Add("```");
                }
            }
        }

        var finalFailureEvent = events.LastOrDefault(entry => string.Equals(entry.Name, "step.failed", StringComparison.Ordinal));
        if (finalFailureEvent is not null)
        {
            AppendFailureEvent(lines, finalFailureEvent.Data);
        }

        if (groupedEvents.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Event Counts");
            lines.AddRange(groupedEvents);
        }

        lines.AddRange([
            string.Empty,
            "## Final Buffers",
            "```json",
            finalBuffersJson,
            "```"
        ]);

        var content = string.Join(Environment.NewLine, lines);
        await File.WriteAllTextAsync(session.SummaryPath, content, cancellationToken);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }

    private static void AppendFailureEvent(List<string> lines, IReadOnlyDictionary<string, object?> data)
    {
        if (lines.Contains("## Final Error", StringComparer.Ordinal))
        {
            return;
        }

        var failureStage = ReadString(data, "failureStage");
        var errorType = ReadString(data, "errorType");
        var errorMessage = ReadString(data, "errorMessage");
        var errorSummary = ReadString(data, "errorSummary");
        var errorDetails = ReadString(data, "errorDetails");

        if (string.IsNullOrWhiteSpace(errorSummary) && string.IsNullOrWhiteSpace(errorDetails))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("## Final Error");

        if (!string.IsNullOrWhiteSpace(failureStage))
        {
            lines.Add($"Failure stage: `{failureStage}`");
        }

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            lines.Add($"Error type: `{errorType}`");
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            lines.Add($"Message: `{errorMessage}`");
        }

        if (!string.IsNullOrWhiteSpace(errorSummary))
        {
            lines.Add($"Summary: `{errorSummary}`");
        }

        if (!string.IsNullOrWhiteSpace(errorDetails))
        {
            lines.Add("```text");
            lines.Add(errorDetails);
            lines.Add("```");
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key)
    {
        return data.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }
}
