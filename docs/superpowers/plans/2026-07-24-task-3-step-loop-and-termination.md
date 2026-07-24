# Step Loop And Termination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the C# host execute multiple decision steps, seed a configurable root goal, and stop on explicit terminal conditions without treating rule exhaustion as an error.

**Architecture:** Return a structured `StepResult` from `HarnessCore`, keep reward/clock orchestration outside the core, and extract the host epoch loop into `HarnessEpochExecutor`. Seed the intention module through its existing buffer-operation API so the runtime does not need a host-only mutation method.

**Tech Stack:** .NET 10, Microsoft.Extensions.Options, xUnit, existing ACT-R protobuf types

---

## File Structure

- `Dotnet/src/Harness.Core/StepResult.cs`: step stop reasons and step metadata
- `Dotnet/src/Harness.Core/HarnessCore.cs`: one-step rule evaluation and operation execution
- `Dotnet/src/Harness.Host/Options/InitialGoalOptions.cs`: root goal configuration type
- `Dotnet/src/Harness.Host/Options/HarnessOptions.cs`: host options with the initial goal section
- `Dotnet/src/Harness.Host/InitialGoalSeeder.cs`: clear and seed the intention goal stack
- `Dotnet/src/Harness.Host/HarnessEpochExecutor.cs`: testable per-epoch loop
- `Dotnet/src/Harness.Host/HarnessRunner.cs`: background-service wrapper over epoch execution
- `Dotnet/src/Harness.Host/Program.cs`: register the seeder and executor
- `Dotnet/src/Harness.Host/appsettings.example.json`: example root goal configuration
- `Dotnet/tests/Harness.Core.Tests/HarnessCoreStepTests.cs`: core step behavior
- `Dotnet/tests/Harness.Core.Tests/InitialGoalSeederTests.cs`: seeder operation payloads
- `Dotnet/tests/Harness.Core.Tests/HarnessEpochExecutorTests.cs`: epoch stop and tick behavior

### Task 1: Add Failing Core Step Tests

**Files:**

- Create: `Dotnet/tests/Harness.Core.Tests/HarnessCoreStepTests.cs`
- Create: `Dotnet/src/Harness.Core/StepResult.cs`
- Modify: `Dotnet/src/Harness.Core/HarnessCore.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task StepAsync_returns_no_applicable_rule_without_selecting_a_rule()
{
    var procedural = new FakeProceduralMemory();
    var neuro = new FakeNeuroCore { SatisfiedRuleIds = [] };
    var core = CreateCore(procedural, neuro);

    var result = await core.StepAsync();

    Assert.Equal(StepStopReason.NoApplicableRule, result.StopReason);
    Assert.Null(result.SelectedRuleId);
    Assert.Empty(procedural.SelectedRuleIds);
}

[Fact]
public async Task StepAsync_returns_continue_after_a_non_terminal_operation()
{
    var intention = new IntentionModule();
    var procedural = new FakeProceduralMemory
    {
        Conditions = [new ProceduralCondition { RuleId = "rule-init" }],
        Action = new NeuroAction { RuleId = "rule-init" }
    };
    var neuro = new FakeNeuroCore
    {
        SatisfiedRuleIds = ["rule-init"],
        Operations = []
    };
    var core = CreateCore(procedural, neuro, intention);

    var result = await core.StepAsync();

    Assert.Equal(StepStopReason.Continue, result.StopReason);
    Assert.Equal("rule-init", result.SelectedRuleId);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
& '.\.dotnet\dotnet.exe' test 'Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj' --filter 'FullyQualifiedName~HarnessCoreStepTests'
```

Expected: compilation or assertion failures because `StepAsync()` still returns `bool` and no `StepResult` exists.

- [ ] **Step 3: Implement the minimal result model and core behavior**

```csharp
public enum StepStopReason
{
    Continue,
    NoApplicableRule,
    GoalReachedTerminalStatus
}

public sealed record StepResult(
    StepStopReason StopReason,
    IReadOnlyList<string> SatisfiedRuleIds,
    string? SelectedRuleId,
    IReadOnlyList<BufferOperation> AppliedOperations)
{
    public bool IsTerminal => StopReason != StepStopReason.Continue;
}
```

`HarnessCore.StepAsync()` should return `NoApplicableRule` before calling `SelectRule`, apply decoded operations, then inspect the intention buffer for `file_opened` or `done`.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the same command from Step 2.

Expected: all `HarnessCoreStepTests` pass.

### Task 2: Add Initial Goal Configuration And Seeder

**Files:**

- Create: `Dotnet/src/Harness.Host/Options/InitialGoalOptions.cs`
- Modify: `Dotnet/src/Harness.Host/Options/HarnessOptions.cs`
- Create: `Dotnet/src/Harness.Host/InitialGoalSeeder.cs`
- Modify: `Dotnet/src/Harness.Host/appsettings.example.json`
- Create: `Dotnet/tests/Harness.Core.Tests/InitialGoalSeederTests.cs`

- [ ] **Step 1: Write the failing seeder test**

```csharp
[Fact]
public void Seed_emits_clear_and_set_goal_operations()
{
    var intention = new IntentionModule();
    var options = Options.Create(new HarnessOptions
    {
        Training = false,
        MaxEpochs = 1,
        MaxStepsPerEpoch = 3,
        InitialGoal = new InitialGoalOptions
        {
            Id = "CodeSearch",
            Query = "find file explorer code"
        }
    });
    var seeder = new InitialGoalSeeder(intention, options);

    seeder.Seed();

    var state = intention.GetBufferState().Data;
    var goal = state.Fields["current_goal"].StructValue;
    var slots = goal.Fields["slots"].StructValue;

    Assert.Equal("CodeSearch", goal.Fields["id"].StringValue);
    Assert.Equal("find file explorer code", slots.Fields["query"].StringValue);
    Assert.Equal("start", slots.Fields["status"].StringValue);
}
```

- [ ] **Step 2: Run the seeder test and verify it fails**

Run:

```powershell
& '.\.dotnet\dotnet.exe' test 'Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj' --filter 'FullyQualifiedName~InitialGoalSeederTests'
```

Expected: compilation failure because `InitialGoalOptions` and `InitialGoalSeeder` do not exist.

- [ ] **Step 3: Implement the configuration and seeder**

```csharp
public sealed class InitialGoalOptions
{
    public required string Id { get; init; }
    public required string Query { get; init; }
}
```

`InitialGoalSeeder.Seed()` should call `IntentionModule.OperateBuffer()` with:

```csharp
new BufferOperation
{
    TargetModuleId = "intention",
    Command = "clear_goals"
}
```

and then:

```csharp
new BufferOperation
{
    TargetModuleId = "intention",
    Command = "set_goal",
    Params = new Struct
    {
        Fields =
        {
            ["id"] = Value.ForString(options.Id),
            ["slots"] = Value.ForStruct(new Struct
            {
                Fields =
                {
                    ["query"] = Value.ForString(options.Query),
                    ["status"] = Value.ForString("start")
                }
            })
        }
    }
}
```

- [ ] **Step 4: Run the seeder test and verify it passes**

Run the same command from Step 2.

Expected: the seeder test passes.

### Task 3: Extract And Test The Epoch Executor

**Files:**

- Create: `Dotnet/src/Harness.Host/HarnessEpochExecutor.cs`
- Modify: `Dotnet/src/Harness.Host/HarnessRunner.cs`
- Modify: `Dotnet/src/Harness.Host/Program.cs`
- Create: `Dotnet/tests/Harness.Core.Tests/HarnessEpochExecutorTests.cs`

- [ ] **Step 1: Write the failing executor tests**

```csharp
[Fact]
public async Task RunAsync_does_not_tick_when_step_ends_without_a_selected_rule()
{
    var core = new FakeHarnessCore(
        new StepResult(
            StepStopReason.NoApplicableRule,
            [],
            null,
            []));
    var reward = new FakeRewardService();
    var clock = new FakeClock();
    var executor = CreateExecutor(core, reward, clock, maxSteps: 5);

    var result = await executor.RunAsync(CancellationToken.None);

    Assert.Equal(EpochStopReason.NoApplicableRule, result.StopReason);
    Assert.Equal(0, reward.CallCount);
    Assert.Equal(0, clock.TickCount);
}

[Fact]
public async Task RunAsync_ticks_once_for_each_selected_rule()
{
    var core = new FakeHarnessCore(
        new StepResult(StepStopReason.GoalReachedTerminalStatus, ["rule-init"], "rule-init", []));
    var reward = new FakeRewardService();
    var clock = new FakeClock();
    var executor = CreateExecutor(core, reward, clock, maxSteps: 5);

    var result = await executor.RunAsync(CancellationToken.None);

    Assert.Equal(EpochStopReason.GoalReachedTerminalStatus, result.StopReason);
    Assert.Equal(1, reward.CallCount);
    Assert.Equal(1, clock.TickCount);
}
```

- [ ] **Step 2: Run the executor tests and verify they fail**

Run:

```powershell
& '.\.dotnet\dotnet.exe' test 'Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj' --filter 'FullyQualifiedName~HarnessEpochExecutorTests'
```

Expected: compilation failure because `HarnessEpochExecutor` and `EpochStopReason` do not exist.

- [ ] **Step 3: Implement the executor and update the background service**

The executor should:

```csharp
for (var step = 0; step < maxSteps && !cancellationToken.IsCancellationRequested; step++)
{
    var result = await _core.StepAsync();

    if (result.SelectedRuleId is not null)
    {
        var reward = await _rewardService.ComputeRewardAsync(cancellationToken);
        await _clock.TickAsync(new StepState(reward, _options.Training), cancellationToken);
    }

    if (result.IsTerminal)
        return new EpochResult(step + 1, MapStopReason(result.StopReason));

    await Task.Delay(10, cancellationToken);
}

return new EpochResult(maxSteps, EpochStopReason.MaxStepsReached);
```

`HarnessRunner.ExecuteAsync()` should only loop over epochs and call the executor.

- [ ] **Step 4: Run the executor tests and verify they pass**

Run the same command from Step 2.

Expected: all executor tests pass.

### Task 4: Run The Full .NET Verification

**Files:**

- Modify: `Dotnet/src/Harness.Host/appsettings.example.json`
- Modify: `Dotnet/ACT-R-Harness.slnx` only if the new files require explicit solution entries

- [ ] **Step 1: Run the full C# test project**

```powershell
& '.\.dotnet\dotnet.exe' test 'Dotnet/tests/Harness.Core.Tests/Harness.Core.Tests.csproj'
```

Expected: all tests pass with zero failures.

- [ ] **Step 2: Build the full solution**

```powershell
& '.\.dotnet\dotnet.exe' build 'Dotnet/ACT-R-Harness.slnx'
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 3: Commit the implementation**

```powershell
git add Dotnet/src/Harness.Core/StepResult.cs Dotnet/src/Harness.Core/HarnessCore.cs Dotnet/src/Harness.Host/Options/InitialGoalOptions.cs Dotnet/src/Harness.Host/Options/HarnessOptions.cs Dotnet/src/Harness.Host/InitialGoalSeeder.cs Dotnet/src/Harness.Host/HarnessEpochExecutor.cs Dotnet/src/Harness.Host/HarnessRunner.cs Dotnet/src/Harness.Host/Program.cs Dotnet/src/Harness.Host/appsettings.example.json Dotnet/tests/Harness.Core.Tests/HarnessCoreStepTests.cs Dotnet/tests/Harness.Core.Tests/InitialGoalSeederTests.cs Dotnet/tests/Harness.Core.Tests/HarnessEpochExecutorTests.cs
git commit -m "feat: fix host step loop termination"
```
