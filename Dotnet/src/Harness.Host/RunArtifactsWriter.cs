using System.Text.Json;
using Harness.Core;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public sealed record RunArtifactsSession(
    string RunDirectory,
    string TracePath,
    string SummaryPath,
    string ScenarioName,
    int Epoch);

public class RunArtifactsWriter(IOptions<HarnessOptions> options)
{
    private readonly HarnessOptions _options = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RunArtifactsSession StartRun(int epoch)
    {
        var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
        Directory.CreateDirectory(artifactRoot);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var scenarioName = SanitizePathSegment(_options.Scenario.Name);
        var runDirectory = Path.Combine(artifactRoot, $"{timestamp}-{scenarioName}-epoch{epoch:D2}");
        Directory.CreateDirectory(runDirectory);

        return new RunArtifactsSession(
            runDirectory,
            Path.Combine(runDirectory, "trace.jsonl"),
            Path.Combine(runDirectory, "summary.md"),
            _options.Scenario.Name,
            epoch);
    }

    public async Task AppendStepAsync(
        RunArtifactsSession session,
        int stepNumber,
        StepResult result,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            epoch = session.Epoch,
            step = stepNumber,
            result.StopReason,
            result.IsTerminal,
            result.FailureStage,
            result.ErrorType,
            result.ErrorMessage,
            result.ErrorDetails,
            result.SelectedRuleId,
            result.SatisfiedRuleIds,
            result.Operations,
            result.Diagnostics,
            bufferStatesBefore = result.BufferStatesBefore,
            bufferStatesAfter = result.BufferStatesAfter
        }, _jsonOptions);

        await File.AppendAllTextAsync(session.TracePath, line + Environment.NewLine, cancellationToken);
    }

    public async Task WriteSummaryAsync(
        RunArtifactsSession session,
        int totalSteps,
        string stopReason,
        IReadOnlyList<ModuleSnapshot> finalBuffers,
        StepResult? lastResult,
        CancellationToken cancellationToken)
    {
        var finalBuffersJson = JsonSerializer.Serialize(finalBuffers, new JsonSerializerOptions(_jsonOptions)
        {
            WriteIndented = true
        });
        var lines = new List<string>
        {
            "# Run Summary",
            string.Empty,
            $"Scenario: `{session.ScenarioName}`",
            $"Epoch: `{session.Epoch}`",
            $"Finished (UTC): `{DateTimeOffset.UtcNow:O}`",
            $"Total steps: `{totalSteps}`",
            $"Stop reason: `{stopReason}`"
        };

        if (lastResult is not null)
        {
            lines.Add($"Last selected rule: `{lastResult.SelectedRuleId ?? "<none>"}`");
            lines.Add($"Matched rules: `{string.Join(", ", lastResult.SatisfiedRuleIds)}`");

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
}
