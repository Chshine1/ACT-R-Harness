using System.Diagnostics;
using System.Text.Json;
using Harness.Shared.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Harness.Shared.Tests;

public sealed class RunReportTests
{
    [Fact]
    public void FailedStepPreservesLastSuccessfulStepAndFailureCorrelation()
    {
        using var listener = CreateListener();
        using var activity = TracingModel.StartActivity("Harness.Run");
        var builder = new RunReportBuilder(
            "run-1",
            DateTimeOffset.Parse("2026-08-06T00:00:00Z"));

        builder.RecordStep(0, 0, "rule-1", "rule_executed", success: true);

        var exception = new InvalidOperationException("boom");
        builder.RecordFailure(
            FailureReport.FromException(exception, "HarnessCore.StepAsync"),
            epoch: 0,
            step: 1);

        var report = builder.Build(
            "failed",
            "error",
            activity,
            DateTimeOffset.Parse("2026-08-06T00:01:00Z"));

        Assert.Equal("run-1", report.RunId);
        Assert.Equal("failed", report.Status);
        Assert.Equal("rule-1", report.LastSuccessfulStep!.RuleId);
        Assert.Equal(0, report.LastSuccessfulStep.Epoch);
        Assert.Equal(1, report.Failure!.Step);
        Assert.Equal("HarnessCore.StepAsync", report.Failure.Boundary);
        Assert.Equal(activity!.TraceId.ToString(), report.Failure.TraceId);
        Assert.Equal(activity.SpanId.ToString(), report.Failure.SpanId);
    }

    [Fact]
    public void WriterCreatesSnakeCaseSummaryArtifact()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "actr-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var builder = new RunReportBuilder("run-2", DateTimeOffset.UtcNow);
            var report = builder.Build(
                "completed",
                "completed",
                activity: null,
                DateTimeOffset.UtcNow);
            var writer = new RunReportWriter(NullLogger<RunReportWriter>.Instance);

            var path = writer.Write(report, root);
            Assert.NotNull(path);
            using var json = JsonDocument.Parse(File.ReadAllText(path!));

            Assert.Equal(
                Path.Combine(root, "runs", "run-2", "summary.json"),
                path);
            Assert.Equal("1.0", json.RootElement.GetProperty("schema_version").GetString());
            Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("completed", json.RootElement.GetProperty("stop_reason").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FailureReportPrefersAttachedBoundaryOverCallerFallback()
    {
        var exception = new InvalidOperationException("module failure");
        exception.Data[TracingModel.Tags.ObservabilityBoundary] = "FileExplorerModule.OperateBuffer";

        var failure = FailureReport.FromException(exception, "HarnessCore.StepAsync");

        Assert.Equal("FileExplorerModule.OperateBuffer", failure.Boundary);
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TracingModel.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
