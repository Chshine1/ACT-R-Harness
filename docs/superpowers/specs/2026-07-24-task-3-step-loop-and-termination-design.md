# Task 3: Step Loop And Termination Design

## Background

The active rules in `shared/ruleset/lab.yml` require the C# host to execute more than one decision step. The current implementation has two blocking problems:

- `HarnessCore.StepAsync()` returns `true` unconditionally, so the runner treats every executed step as a terminal state.
- `HarnessRunner` assumes that every step produces a selected rule and always computes a reward/ticks the clock, even when no rule is available.

The runtime also has no startup component that seeds the intention buffer with a root goal. Without a goal containing `status = "start"`, `rule-init` cannot match.

## Goal

Make the host run multiple steps with explicit termination semantics, seed a reproducible root goal at the start of each epoch, and treat normal exhaustion as a successful stop rather than an exception.

## Scope

In scope:

- replace the boolean `HarnessCore.StepAsync()` result with a structured step result
- distinguish continuation, no applicable rule, and terminal goal status
- seed a configurable root goal before each epoch
- avoid reward/learning ticks when no rule was selected
- keep `MaxStepsPerEpoch` as an executor-level safety limit
- add focused unit tests for the core step, goal seeding, and epoch execution behavior

Out of scope:

- implementing the commented-out future rules in `lab.yml`
- changing Python gRPC contracts
- adding trace/report output from Task 4
- implementing a generalized runtime state machine
- changing reward or utility-learning algorithms

## Design

### 1. Structured step result

Add a small result model in `Harness.Core`:

- `StepStopReason.Continue`
- `StepStopReason.NoApplicableRule`
- `StepStopReason.GoalReachedTerminalStatus`

`StepResult` contains:

- `StopReason`
- `SatisfiedRuleIds`
- `SelectedRuleId`
- `AppliedOperations`

`StepResult.IsTerminal` is true whenever the stop reason is not `Continue`.

`HarnessCore.StepAsync()` keeps the existing decision flow:

1. collect module buffer states and operation schemas
2. ask procedural memory for all conditions
3. ask neuro core which conditions are satisfied
4. return `NoApplicableRule` if the result is empty
5. select a rule and decode its action
6. apply the decoded operations to local modules
7. inspect the intention buffer after operations
8. return `GoalReachedTerminalStatus` when the current goal status is `file_opened` or `done`; otherwise return `Continue`

The selected rule ID comes from the returned `NeuroAction.RuleId`. The core does not compute rewards or tick the clock.

### 2. Configurable root goal

Extend host options with a required `InitialGoalOptions` section:

- `Id`
- `Query`

The `status` slot is always created by code as `"start"` and is not configurable. The example configuration uses:

```json
"InitialGoal": {
  "Id": "CodeSearch",
  "Query": "find the code related to file exploration"
}
```

Add `InitialGoalSeeder` in `Harness.Host`. It depends on `IntentionModule` and sends two existing module operations:

1. `clear_goals`
2. `set_goal` with the configured ID and slots `{ query, status: "start" }`

Seeding occurs at the beginning of every epoch, ensuring that a previous `ExploreFileSystem` subgoal cannot leak into the next run.

### 3. Testable epoch execution

Keep `HarnessRunner` as the `BackgroundService` shell and extract the epoch loop into `HarnessEpochExecutor`.

The executor:

- seeds the root goal
- calls `HarnessCore.StepAsync()`
- computes reward and ticks the clock only when `SelectedRuleId` is not null
- stops on a terminal `StepResult`
- stops on cancellation
- stops with `MaxStepsReached` when the configured step limit is exhausted

This avoids an extra learning tick after a no-rule terminal step and makes the loop testable without starting a host process.

### 4. Error handling

No applicable rule is a normal terminal result. `HarnessCore` must not call `SelectRule` when the satisfied-rule list is empty.

If a decoded operation targets an unknown module, preserve the existing failure behavior because that indicates a malformed neuro-core response rather than normal exhaustion.

### 5. Tests

Add focused tests to the existing `Harness.Core.Tests` project:

- `HarnessCoreStepTests`
  - empty satisfied-rule list returns `NoApplicableRule`
  - a normal rule execution returns `Continue`
  - a rule that changes intention status to `file_opened` returns `GoalReachedTerminalStatus`
  - result metadata preserves satisfied IDs, selected ID, and applied operations
- `InitialGoalSeederTests`
  - emits `clear_goals`
  - emits `set_goal`
  - preserves configured ID/query and injects `status = "start"`
- `HarnessEpochExecutorTests`
  - seeds once per epoch
  - ticks only after a selected rule
  - stops on no applicable rule
  - stops at the configured maximum step count

Use in-memory fake `IModule`, `IProceduralMemory`, `INeuroCore`, `IClock`, and `IRewardService` implementations. No Python service or network process is required.

## File Changes

Expected files to modify or create:

- Create: `Dotnet/src/Harness.Core/StepResult.cs`
- Modify: `Dotnet/src/Harness.Core/HarnessCore.cs`
- Create: `Dotnet/src/Harness.Host/Options/InitialGoalOptions.cs`
- Modify: `Dotnet/src/Harness.Host/Options/HarnessOptions.cs`
- Create: `Dotnet/src/Harness.Host/InitialGoalSeeder.cs`
- Create: `Dotnet/src/Harness.Host/HarnessEpochExecutor.cs`
- Modify: `Dotnet/src/Harness.Host/HarnessRunner.cs`
- Modify: `Dotnet/src/Harness.Host/Program.cs`
- Modify: `Dotnet/src/Harness.Host/appsettings.example.json`
- Create: `Dotnet/tests/Harness.Core.Tests/HarnessCoreStepTests.cs`
- Create: `Dotnet/tests/Harness.Core.Tests/InitialGoalSeederTests.cs`
- Create: `Dotnet/tests/Harness.Core.Tests/HarnessEpochExecutorTests.cs`

## Acceptance Criteria

Task 3 is complete when:

- `HarnessCore.StepAsync()` no longer returns a boolean terminal signal
- multiple non-terminal steps can execute in one epoch
- no applicable rule ends the epoch without throwing
- a root goal is seeded before each epoch with `status = "start"`
- `file_opened` and `done` stop the current demo loop
- reward/clock ticks occur only after an actual rule selection
- max-step exhaustion remains a normal stop
- focused tests cover the core, seeder, and executor behavior
