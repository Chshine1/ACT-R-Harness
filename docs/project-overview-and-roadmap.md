# ACT-R Harness: Project Overview and Roadmap

Status date: 2026-07-21

This document is the working source of truth for the current implementation status and the near-term demo plan. The
top-level `README.md` still describes the broader research direction, but collaborators working on the code today should
use this file to understand what the project is trying to run next and where the gaps are.

## Current Objective

The current goal is to run the harness end to end with the active procedural rules in `shared/ruleset/lab.yml`.

The deliverable for this phase is a minimal codebase-navigation demo in which:

- Python provides the gRPC services for declarative memory, procedural memory, and neuro core.
- The C# host owns runtime buffers/modules, executes decoded operations, and drives the step loop.
- The host starts from a seeded goal, runs until completion or exhaustion, and writes useful logs and a simple report.

For this phase, "complete harness" means the active rules in `shared/ruleset/lab.yml` run correctly. The commented-out
future rules in that file are not required for the first demo.

## What This Project Is

At a high level, ACT-R Harness is a neuro-symbolic runtime split across shared protobuf contracts, Python services, and
a C# host:

- `shared/proto`: common gRPC contracts used by both runtimes.
- `shared/ruleset/lab.yml`: the current procedural ruleset for the codebase-navigation scenario.
- `python/src/actr_harness`: gRPC services for declarative memory, procedural memory, and neuro core.
- `python/tests`: tests that exercise neuro-core condition evaluation and action decoding against `lab.yml`.
- `Dotnet/src/Harness.Core`: the step loop, module registry, gRPC clients, and core modules.
- `Dotnet/src/Harness.Codebase`: code-navigation modules such as file exploration and code viewport.
- `Dotnet/src/Harness.Host`: the executable host that wires services together and runs epochs/steps.

The intended runtime split is:

1. The C# host gathers buffer states from modules.
2. The host asks Python procedural memory for all rule conditions.
3. The host asks Python neuro core which rules are satisfied.
4. The host asks Python procedural memory to choose one rule.
5. The host asks Python neuro core to decode that rule's action into concrete `BufferOperation`s.
6. The C# host applies those operations to local modules.
7. The host records the step trace and continues until termination.

## Active Rules In `lab.yml`

The current demo rules define a small codebase-navigation loop:

- `rule-init`: if `intention.current_goal.slots.status == "start"`, query declarative memory and move the goal status to
  `memory_queried`.
- `rule-memory-hit`: if status is `memory_queried` and a chunk was retrieved, derive attention tags, optionally open the
  file from memory, and move the goal status to `file_opened`.
- `rule-memory-miss`: if status is `memory_queried` and no chunk was retrieved, push an `ExploreFileSystem` subgoal.

The later file-exploration and viewport rules are present only as commented drafts. They belong to the next phase, not
the first minimal demo.

## Current State

What is already in place:

- Shared protobuf contracts exist for declarative memory, procedural memory, neuro core, and perception/motor services.
- Python neuro core tests exist for the current `lab.yml` conditions and decoded actions:
    - `python/tests/test_neuro_core_condition.py`
    - `python/tests/test_neuro_core_decode.py`
- The C# runtime already has working module abstractions and local buffer modules for:
    - `declarative_memory`
    - `intention`
    - `file_explorer`
    - `code_viewport`

What is still mismatched:

- The repository vision in `README.md` is broader than the runnable implementation.

## Concrete Gaps Blocking The Demo

The main blockers are visible in the current code:

- `python/src/actr_harness/services/procedural_memory.py`
    - `self.rules` is initialized but never populated.
    - The procedural-memory service does not currently load `shared/ruleset/lab.yml`, so `GetAllConditions` would return
      no rules.
- `Dotnet/src/Harness.Core/Extensions/DependencyInjectionExtensions.cs`
    - Only `DeclarativeMemoryModule` and `IntentionModule` are registered.
    - The active rules also need `FileExplorerModule` and `CodeViewportModule`.
- `Dotnet/src/Harness.Abstractions/IEmbeddingService.cs`
    - `FileExplorerModule` depends on an embedding service, but no implementation is present.
- `Dotnet/src/Harness.Core/HarnessCore.cs`
    - `StepAsync()` currently returns `true` unconditionally, which causes the runner to stop after one step.
- `Dotnet/src/Harness.Host/HarnessRunner.cs`
    - The loop expects `StepAsync()` to signal termination, but the current core behavior makes every epoch terminate
      immediately.
- `Dotnet/src/Harness.Host/Program.cs` and `Dotnet/src/Harness.Host/Options/GrpcClientsOptions.cs`
    - Host config still expects `FrostpunkWorldAddress`, which is not part of the current demo path.
- No current component seeds an initial intention goal such as `{ id, query, status = "start" }`, so `rule-init` has
  nothing to match against.
- There is no demo-grade logging/report pipeline yet. We need a step trace that other developers can inspect without a
  debugger.

## Minimal Demo Scope

The first demo should stay intentionally small:

- Use only the three active rules in `shared/ruleset/lab.yml`.
- Seed one root goal in the intention module at startup.
- Demonstrate both paths:
    - memory hit
    - memory miss
- Log every step and save a simple report to disk.
- Prefer deterministic or near-deterministic behavior where possible.

Recommended non-goals for the first demo:

- Do not implement the commented-out later rules yet.
- Do not optimize utility learning yet.
- Do not keep the Frostpunk/household environment pieces in the critical path.
- Do not require an external UI.

## Recommended Workstreams

These tasks can be worked on in parallel by different collaborators.

### 1. Python Service Parity

Goal: make procedural memory actually serve the current ruleset.

Expected outputs:

- Load `shared/ruleset/lab.yml` on startup.
- Convert YAML rules into `ProceduralCondition` and `NeuroAction` objects.
- Set a stable initial utility for each rule.
- Add a small unit test that verifies the service loads the three active rules.

### 2. C# Host Wiring

Goal: register the modules required by the active rules.

Expected outputs:

- Reference `Harness.Codebase` from the host/core path.
- Register `FileExplorerModule` and `CodeViewportModule`.
- Add a simple `IEmbeddingService` implementation for the demo.

Recommended approach:

- For the minimal demo, a local deterministic fallback is enough. It can be token-overlap based or even return simple
  fixed vectors, as long as `FileExplorerModule` can run without external dependencies.

### 3. Step Loop And Termination

Goal: make the host run more than one step and stop for the right reasons.

Expected outputs:

- Change `HarnessCore.StepAsync()` to return meaningful termination information.
- Handle "no satisfied rule" as a normal end state, not an unhandled failure.
- Seed the initial goal before the first step.
- Define a clear stop condition for the first demo, such as:
    - no applicable rule
    - max steps reached
    - active goal reaches a terminal status

### 4. Logging And Reports

Goal: make each run inspectable by other developers.

Expected outputs:

- Per-step structured logs containing:
    - step number
    - satisfied rule IDs
    - selected rule ID
    - decoded operations
    - buffer states before and after the step
    - errors and stop reason
- A saved report artifact, for example:
    - `artifacts/runs/<timestamp>/trace.jsonl`
    - `artifacts/runs/<timestamp>/summary.md`

For the first demo, plain JSONL plus a short Markdown summary is enough.

### 5. Demo Packaging

Goal: make the demo easy to run for someone joining the project.

Expected outputs:

- A real `appsettings.json` example for the host.
- Exact startup commands for Python and C#.
- One documented sample scenario, including the initial goal and expected outcome.

## Definition Of Done For The Minimal Demo

The phase is complete when all of the following are true:

- Python procedural memory loads the active rules from `shared/ruleset/lab.yml`.
- The C# host starts with a seeded goal and can execute multiple steps.
- The host has all modules required by the active rules:
    - `declarative_memory`
    - `intention`
    - `file_explorer`
    - `code_viewport`
- At least one run demonstrates a memory-hit path.
- At least one run demonstrates a memory-miss path.
- Each run emits readable logs and a saved report.
- A new collaborator can follow the setup instructions and reproduce the demo without reading code first.

## Suggested Execution Order

If one person is doing the work sequentially, this order is the shortest path:

1. Load rules into Python procedural memory.
2. Register the missing C# modules and add a minimal embedding service.
3. Seed the initial goal and fix loop termination.
4. Add logging/report output.
5. Add one end-to-end smoke test or reproducible manual demo script.

## Next Phase After The Minimal Demo

Once the first demo is stable, the next useful expansion is:

1. Implement the commented-out `lab.yml` rules for exploring files and locating specific lines.
2. Replace placeholder reward/training pieces with scenario-appropriate logic, or disable them entirely for non-training
   demos.
3. Add richer integration tests that exercise the host and Python services together.
4. Reconcile the top-level README with the actual runnable scenarios so architecture, demo scope, and research roadmap
   are clearly separated.

## Working Principle For Contributors

For now, treat the codebase-navigation harness as the operational target and the broader ACT-R research framing as the
long-term direction. The fastest way to make the project collaborative is to keep the first demo narrow, observable, and
reproducible.
