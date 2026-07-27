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
            result.ErrorMessage,
            result.SelectedRuleId,
            result.SatisfiedRuleIds,
            result.Operations,
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
        CancellationToken cancellationToken)
    {
        var finalBuffersJson = JsonSerializer.Serialize(finalBuffers, new JsonSerializerOptions(_jsonOptions)
        {
            WriteIndented = true
        });

        var content = string.Join(Environment.NewLine, [
            "# Run Summary",
            string.Empty,
            $"Scenario: `{session.ScenarioName}`",
            $"Epoch: `{session.Epoch}`",
            $"Finished (UTC): `{DateTimeOffset.UtcNow:O}`",
            $"Total steps: `{totalSteps}`",
            $"Stop reason: `{stopReason}`",
            string.Empty,
            "## Final Buffers",
            "```json",
            finalBuffersJson,
            "```"
        ]);

        await File.WriteAllTextAsync(session.SummaryPath, content, cancellationToken);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }
}
