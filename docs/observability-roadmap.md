# Observability Roadmap

Status date: 2026-08-06

This roadmap covers observability for the current .NET-only runtime. The goal is to formalize a tracing model first,
then add a logging model on top of it, and make exceptions part of the same contract so failures are easy to inspect
after a run.

## Current State

- The harness already runs in-process on the .NET side.
- OpenTelemetry-compatible tracing is already present through the existing AOP-style `TraceSpanAttribute`.
- The host already emits structured JSON logs, but the project does not yet have a single observability model that
  defines span hierarchy, logging fields, and exception handling together.
- Some failures are visible today, but they are not yet normalized into a consistent run-level trace and summary.

## Target Observability Model

### Tracing

- Keep `TraceSpanAttribute` as the default way to define spans at meaningful boundaries.
- Use a single run root span for each host execution, then nest epoch spans, step spans, and component spans beneath it.
- Treat the following as first-class tracing boundaries:
  - run lifecycle
  - epoch lifecycle
  - step evaluation
  - rule evaluation and rule selection
  - action decoding
  - module operations
  - external calls, especially LLM requests
- Use span events for important state changes that do not deserve their own span, such as:
  - satisfied rule IDs
  - selected rule ID
  - decoded operation list
  - stop reason
  - exception details
- Standardize span attributes so traces can be searched consistently:
  - `run.id`
  - `epoch.index`
  - `step.index`
  - `rule.id`
  - `module.id`
  - `operation.command`
  - `stop.reason`
  - `exception.type`
  - `exception.message`

### Logging

- Use structured logs as the readable companion to traces.
- Every important lifecycle event should log the same identifiers used in spans so logs and traces can be correlated.
- Recommended log fields:
  - `run_id`
  - `trace_id`
  - `span_id`
  - `epoch`
  - `step`
  - `rule_id`
  - `module_id`
  - `stop_reason`
  - `exception_type`
  - `exception_message`
- Severity should be consistent:
  - `Information` for lifecycle milestones and successful step summaries
  - `Debug` for payload-level details such as decoded operations and buffer snapshots
  - `Warning` for recoverable irregularities
  - `Error` for terminal failures

### Exceptions

- Catch exceptions at the run boundary and step boundary so failures are always recorded.
- Mark the active span as error when an exception escapes a traced boundary.
- Log the exception once at `Error` with enough context to reproduce the failure.
- Convert terminal failures into explicit stop reasons so the run summary can explain why execution ended.
- Preserve the last successful step and the failing boundary in the final report.

## Roadmap

### Phase 1: Tracing Model

- Define the span hierarchy and naming conventions.
- Expand the current attribute-based tracing so the important runtime boundaries are covered consistently.
- Add explicit span events for rule selection, action decoding, module operations, and stop reasons.

### Phase 2: Logging Model

- Define the shared structured log schema.
- Log one coherent step summary per decision cycle.
- Ensure logs always carry trace correlation fields.

### Phase 3: Exception Handling And Reporting

- Standardize exception capture at the host, core, and module boundaries.
- Emit a clear terminal failure summary for runs that stop because of an error.
- Make failures inspectable without a debugger by combining trace status, logs, and the final report artifact.

## Validation

- A successful run shows a complete trace tree from run to step to rule evaluation to action execution.
- A failing run marks the correct span as error and records one clear terminal log entry.
- Logs and traces can be joined through shared correlation fields.
- The final summary makes it obvious where the run stopped and why.

## Non-Goals

- No new product features outside observability.
- No reintroduction of the Python service split.
- No UI or dashboard work.
- No unrelated refactors.
