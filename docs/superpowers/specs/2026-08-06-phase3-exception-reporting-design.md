# Phase 3 Exception Handling And Reporting Design

## Goal

Make every .NET harness run diagnosable without a debugger by standardizing exception
snapshots, stopping failed runs with an explicit outcome, and writing a final JSON
summary artifact that can be joined to the existing trace and structured logs.

## Scope

This phase covers:

- Standard exception snapshots at the core and host boundaries.
- Propagating a step failure into the run-level outcome.
- Preserving the last successful step before a failure.
- Writing `artifacts/runs/<run_id>/summary.json` for completed, failed, and canceled runs.
- Logging the report path and terminal outcome through the existing `LoggingModel`.

This phase does not add a UI, dashboard, external telemetry exporter, Markdown report,
or a new execution service.

## Architecture

### Shared report contract

Add report types under `Harness.Shared.Observability`:

- `FailureReport`: boundary, exception type/message/stacktrace, optional epoch/step,
  and trace/span correlation IDs.
- `SuccessfulStepReport`: epoch, step, selected rule, and stop reason.
- `RunReport`: schema version, run identity, status, stop reason, trace/span IDs,
  timestamps, last successful step, and failure details.
- `RunReportBuilder`: mutable run-local accumulator that records steps and failures
  and creates an immutable `RunReport`.

The report contract uses explicit snake_case JSON property names so the artifact is
stable independently of serializer defaults.

### Host writer

Add `RunReportWriter` under `Harness.Shared.Observability`. It receives a completed
`RunReport` and an artifact root, creates `runs/<run_id>`, serializes the report to a
temporary file, then replaces `summary.json` atomically. A write failure is logged
with the existing structured logging model and does not hide the original run
exception.

### Runtime flow

1. `HarnessRunner` creates a `RunReportBuilder` when `ExecuteAsync` starts.
2. Each step records its current epoch/step and passes its `StepResult` outcome to
   the builder.
3. `HarnessCore` converts caught exceptions into `FailureReport` data on the
   terminal `StepResult`, while retaining the existing trace exception event and
   error log.
4. A failed step stops further execution and sets the run status to `failed`.
5. Host-level exceptions are captured at `ExecuteAsync`, using the current run
   context when no step failure exists.
6. The `finally` block builds and writes the report before stopping the host.

Run status values are `completed`, `failed`, and `canceled`. Normal terminal
conditions such as `no_rule_loaded` remain completed outcomes; `error` and uncaught
host exceptions are failed outcomes.

## JSON Schema

The artifact contains these top-level fields:

```json
{
  "schema_version": "1.0",
  "run_id": "string",
  "status": "completed|failed|canceled",
  "stop_reason": "string",
  "trace_id": "string|null",
  "span_id": "string|null",
  "started_at_utc": "ISO-8601 timestamp",
  "completed_at_utc": "ISO-8601 timestamp",
  "last_successful_step": {
    "epoch": 0,
    "step": 0,
    "rule_id": "string|null",
    "stop_reason": "string"
  },
  "failure": {
    "boundary": "string",
    "epoch": 0,
    "step": 0,
    "trace_id": "string|null",
    "span_id": "string|null",
    "exception_type": "string",
    "exception_message": "string",
    "exception_stacktrace": "string"
  }
}
```

`last_successful_step` and `failure` are nullable. A successful run has no failure;
a failed run must have failure details unless the report writer itself failed.

## Error Handling

- Core failures are terminal and return a failed `StepResult`; they are not silently
  treated as an ordinary terminal step.
- The runner records the failure once in the report and emits the existing exception
  log plus one report outcome log.
- Cancellation is not reported as an exception; it produces `status=canceled` and
  `stop_reason=canceled`.
- Report serialization and filesystem errors are reported through `LoggingModel` but
  are not allowed to replace the original execution exception.

## Validation

- Builder tests prove that a failed step preserves the preceding successful step and
  includes failure boundary, exception, and trace/span fields.
- Writer tests prove the exact path and snake_case JSON fields.
- A normal run produces `status=completed`.
- A core exception produces `status=failed`, `stop_reason=error`, and a failure object.
- A canceled run produces `status=canceled` without an exception object.
- `git diff --check`, build, and the shared test project are run before completion.
