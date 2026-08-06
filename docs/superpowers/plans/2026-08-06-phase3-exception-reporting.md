# Phase 3 Exception Reporting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make each .NET harness run produce an inspectable JSON outcome with standardized exception details, failure correlation, and the last successful step.

**Architecture:** Keep the report contract and builder in `Harness.Shared.Observability`, so the host and core share the same failure schema without a dependency cycle. `HarnessCore` returns a `FailureReport` on terminal step errors, `HarnessRunner` owns run outcome state and cancellation semantics, and `RunReportWriter` atomically writes `ArtifactRoot/runs/<run_id>/summary.json` without masking execution failures.

**Tech Stack:** C#/.NET 10, `System.Text.Json`, `System.Diagnostics.Activity`, existing `LoggingModel` and `TracingModel`, xUnit.

---

### Task 1: Lock the report contract with failing tests

**Files:**
- Create: `Dotnet/tests/Harness.Shared.Tests/RunReportTests.cs`
- Modify: `Dotnet/src/Harness.Shared/Observability/LoggingModel.cs`

- [ ] **Step 1: Add builder and writer behavior tests**

Create tests that:

```csharp
[Fact]
public void FailedStepPreservesLastSuccessfulStepAndFailureCorrelation()
{
    using var listener = CreateListener();
    using var activity = TracingModel.StartActivity("Harness.Run");
    var builder = new RunReportBuilder("run-1", DateTimeOffset.Parse("2026-08-06T00:00:00Z"));
    builder.RecordStep(0, 0, "rule-1", "rule_executed", success: true);

    var exception = new InvalidOperationException("boom");
    builder.RecordFailure(FailureReport.FromException(exception, "HarnessCore.StepAsync"), 0, 1);

    var report = builder.Build("failed", "error", activity, DateTimeOffset.Parse("2026-08-06T00:01:00Z"));

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
    var root = Path.Combine(Path.GetTempPath(), "actr-report-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var builder = new RunReportBuilder("run-2", DateTimeOffset.UtcNow);
        var report = builder.Build("completed", "completed", activity: null, DateTimeOffset.UtcNow);
        var writer = new RunReportWriter(NullLogger<RunReportWriter>.Instance);

        var path = writer.Write(report, root);
        using var json = JsonDocument.Parse(File.ReadAllText(path));

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
```

The test helper should install an `ActivityListener` for `TracingModel.ActivitySourceName` and sample all data, matching `TracingModelTests`.

- [ ] **Step 2: Run the focused test to verify RED**

Run:

```powershell
dotnet test Dotnet/tests/Harness.Shared.Tests/Harness.Shared.Tests.csproj --filter FullyQualifiedName~RunReportTests
```

Expected result: the project cannot compile until the report types exist; on the current machine the command may stop earlier with `NETSDK1045` because only SDK 9 is installed for a `net10.0` project.

### Task 2: Implement the shared report model, builder, and writer

**Files:**
- Create: `Dotnet/src/Harness.Shared/Observability/RunReportModel.cs`
- Create: `Dotnet/src/Harness.Shared/Observability/RunReportBuilder.cs`
- Create: `Dotnet/src/Harness.Shared/Observability/RunReportWriter.cs`
- Modify: `Dotnet/src/Harness.Shared/Observability/LoggingModel.cs`
- Test: `Dotnet/tests/Harness.Shared.Tests/RunReportTests.cs`

- [ ] **Step 1: Add the JSON contract**

Define records with explicit `[JsonPropertyName]` attributes:

```csharp
public sealed record FailureReport(
    string Boundary,
    int? Epoch,
    int? Step,
    string? TraceId,
    string? SpanId,
    string ExceptionType,
    string ExceptionMessage,
    string ExceptionStacktrace)
{
    public static FailureReport FromException(Exception exception, string boundary)
    {
        var activity = Activity.Current;
        return new(
            boundary,
            null,
            null,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString());
    }
}

public sealed record SuccessfulStepReport(int Epoch, int Step, string? RuleId, string StopReason);

public sealed record RunReport(
    string SchemaVersion,
    string RunId,
    string Status,
    string StopReason,
    string? TraceId,
    string? SpanId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    SuccessfulStepReport? LastSuccessfulStep,
    FailureReport? Failure);
```

Use `"1.0"` as the schema version. Preserve nullable report fields in the JSON output.

- [ ] **Step 2: Add the run-local builder**

Implement:

```csharp
public sealed class RunReportBuilder
{
    public RunReportBuilder(string runId, DateTimeOffset startedAtUtc);
    public void SetCurrentStep(int epoch, int step);
    public void RecordStep(int epoch, int step, string? ruleId, string stopReason, bool success);
    public void RecordFailure(FailureReport failure, int? epoch = null, int? step = null);
    public RunReport Build(string status, string stopReason, Activity? activity, DateTimeOffset completedAtUtc);
}
```

`RecordStep` updates `LastSuccessfulStep` only when `success` is true. `RecordFailure` applies the supplied epoch/step over nullable failure location fields and keeps the first failure, so a report cannot be overwritten by cleanup errors.

- [ ] **Step 3: Add atomic JSON writing**

Implement `RunReportWriter(ILogger<RunReportWriter> logger)` with:

```csharp
public string? Write(RunReport report, string artifactRoot);
```

Write `JsonSerializer.Serialize(report, options)` to a uniquely named temporary file under `runs/<run_id>`, replace `summary.json` with `File.Move(temp, final, overwrite: true)`, and return the final path. On an I/O or serialization exception, call `LoggingModel.LogException` with boundary `RunReportWriter.Write`, clean up the temporary file if possible, return `null`, and never throw from the writer. The runner will call this from `finally`, so a report write failure cannot hide the original execution exception.

- [ ] **Step 4: Add report logging fields/events**

Add `LoggingModel.Events.RunReportWritten` and `LoggingModel.Fields.ArtifactPath`/`ReportStatus`. Log successful report writes at `Information`, canceled reports at `Warning`, and failed reports at `Error`, all with `run_id`, `stop_reason`, `status`, and `artifact_path`.

- [ ] **Step 5: Run the focused tests**

Run:

```powershell
dotnet test Dotnet/tests/Harness.Shared.Tests/Harness.Shared.Tests.csproj --filter FullyQualifiedName~RunReportTests
```

Expected result: PASS when a .NET 10 SDK is available; otherwise record the exact SDK failure without changing the project target framework.

### Task 3: Propagate standardized failures from core and tracing

**Files:**
- Modify: `Dotnet/src/Harness.Core/StepResult.cs`
- Modify: `Dotnet/src/Harness.Core/HarnessCore.cs`
- Modify: `Dotnet/src/Harness.Shared/Observability/TraceSpanAttribute.cs`
- Test: `Dotnet/tests/Harness.Shared.Tests/TracingModelTests.cs`

- [ ] **Step 1: Add cancellation and core failure expectations**

Extend tracing tests to assert a canceled traced task does not add an `exception` event or mark its span `Error`. Add a core-level test seam only if the existing test project can construct `HarnessCore` without external LLM configuration; otherwise cover the `FailureReport.FromException` contract in `RunReportTests`.

- [ ] **Step 2: Update `StepResult`**

Add an optional `FailureReport? Failure` property after the existing operation data:

```csharp
public sealed record StepResult(
    bool IsTerminal,
    string StopReason,
    string? SelectedRuleId = null,
    IReadOnlyList<OperationTrace>? Operations = null,
    FailureReport? Failure = null);
```

- [ ] **Step 3: Preserve cancellation in `HarnessCore`**

Add:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    throw;
}
```

before the general exception catch. In the general catch, create `FailureReport.FromException(exception, nameof(StepAsync))`, pass it to `LoggingModel.LogException`, and return the terminal error `StepResult` with `Failure`.

- [ ] **Step 4: Treat cancellation as cancellation in `TraceSpanAttribute`**

Add a `SetCanceled()` path that marks the span complete without recording an exception event. Use it for canceled tasks and synchronous `OperationCanceledException`; retain `SetError` for all other exceptions.

- [ ] **Step 5: Run tracing and report tests**

Run:

```powershell
dotnet test Dotnet/tests/Harness.Shared.Tests/Harness.Shared.Tests.csproj --filter "FullyQualifiedName~TracingModelTests|FullyQualifiedName~RunReportTests"
```

Expected result: all selected tests pass with .NET 10; the current environment may stop at `NETSDK1045`.

### Task 4: Integrate run outcome and final artifact in the host

**Files:**
- Modify: `Dotnet/src/Harness.Host/HarnessRunner.cs`
- Modify: `Dotnet/src/Harness.Host/Program.cs`
- Modify: `Dotnet/src/Harness.Shared/Observability/LoggingModel.cs`

- [ ] **Step 1: Register the writer**

Add `services.AddSingleton<RunReportWriter>();` in `Program.ConfigureServices`, and inject `RunReportWriter reportWriter` into `HarnessRunner`.

- [ ] **Step 2: Start and track a builder**

Create `RunReportBuilder` at the beginning of `ExecuteAsync` with `_runId` and `DateTimeOffset.UtcNow`. Track local `status = "completed"` and `stopReason = "completed"` values.

- [ ] **Step 3: Record each step**

Set the current step before calling `core.StepAsync`. After the result returns, call `reportBuilder.RecordStep` with `success: lastResult.StopReason != "error"` and call `RecordFailure(lastResult.Failure, epochIndex, stepIndex)` when present. Keep the existing coherent step summary log.

- [ ] **Step 4: Stop on terminal outcomes**

Change `RunSingleStepAsync` to return `Task<StepResult>`, make `RunEpochAsync` return `Task<StepResult?>` containing the first terminal result, and stop the run when a terminal result is returned. Set the run stop reason to the terminal result's reason; set status to `failed` for `"error"` and `completed` for normal terminal reasons.

- [ ] **Step 5: Capture host exceptions and cancellation**

In the existing catches:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    status = "canceled";
    stopReason = "canceled";
    throw;
}
catch (Exception exception)
{
    status = "failed";
    stopReason = "error";
    reportBuilder.RecordFailure(
        FailureReport.FromException(exception, nameof(ExecuteAsync)));
    throw;
}
```

Keep `finally` responsible for building/writing the report before `StopApplication`. If a failed step is returned normally from core, set `status = "failed"` without throwing so the report is written with the correct failure object.

- [ ] **Step 6: Run a host build**

Run:

```powershell
dotnet build Dotnet/src/Harness.Host/Harness.Host.csproj --no-restore
```

Expected result: successful build with .NET 10; on this machine the known `NETSDK1045` failure is expected until SDK 10 is installed.

### Task 5: Full verification and scope review

**Files:**
- Modify: `docs/observability-roadmap.md` only if implementation status wording needs a factual update.

- [ ] **Step 1: Run all shared tests**

```powershell
dotnet test Dotnet/tests/Harness.Shared.Tests/Harness.Shared.Tests.csproj --no-restore
```

- [ ] **Step 2: Run formatting and repository checks**

```powershell
git diff --check
git status --short
```

Confirm no unrelated files changed and that `summary.json` is the only new runtime artifact format.

- [ ] **Step 3: Review report behavior**

Verify the implementation has:

- `completed` reports with no failure.
- `failed` reports with `stop_reason=error`, exception details, failure boundary, and last successful step.
- `canceled` reports without an exception object.
- Shared `run_id` plus trace/span IDs for joining logs, traces, and reports.
- Writer failures logged without replacing the original run exception.

- [ ] **Step 4: Commit the implementation**

```powershell
git add Dotnet docs/observability-roadmap.md
git commit -m "feat: add phase 3 failure reports"
```
