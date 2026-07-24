using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Harness.Core;
using Harness.Core.Modules;

namespace Harness.Core.Tests;

public class HarnessCoreStepTests
{
    [Fact]
    public async Task StepAsync_returns_no_applicable_rule_without_selecting_a_rule()
    {
        var procedural = new FakeProceduralMemory();
        var neuro = new FakeNeuroCore { SatisfiedRuleIds = [] };
        var core = CreateCore(procedural, neuro, new IntentionModule());

        var result = await core.StepAsync();

        Assert.Equal(StepStopReason.NoApplicableRule, result.StopReason);
        Assert.True(result.IsTerminal);
        Assert.Null(result.SelectedRuleId);
        Assert.Empty(result.AppliedOperations);
        Assert.Empty(procedural.SelectionInputs);
    }

    [Fact]
    public async Task StepAsync_returns_continue_and_preserves_result_metadata_for_non_terminal_operations()
    {
        var intention = new IntentionModule();
        SeedGoal(intention);

        var operation = new BufferOperation
        {
            TargetModuleId = "intention",
            Command = "modify_slot",
            Params = new Struct
            {
                Fields =
                {
                    ["slot"] = Value.ForString("status"),
                    ["slot_value"] = Value.ForString("memory_queried")
                }
            }
        };

        var procedural = new FakeProceduralMemory
        {
            Conditions = [new ProceduralCondition { RuleId = "rule-init" }],
            SelectedAction = new NeuroAction { RuleId = "rule-init" }
        };
        var neuro = new FakeNeuroCore
        {
            SatisfiedRuleIds = ["rule-init"],
            Operations = [operation]
        };
        var core = CreateCore(procedural, neuro, intention);

        var result = await core.StepAsync();

        Assert.Equal(StepStopReason.Continue, result.StopReason);
        Assert.False(result.IsTerminal);
        Assert.Equal(["rule-init"], result.SatisfiedRuleIds);
        Assert.Equal("rule-init", result.SelectedRuleId);
        Assert.Single(result.AppliedOperations);

        var currentGoal = intention.GetBufferState().Data.Fields["current_goal"].StructValue;
        var slots = currentGoal.Fields["slots"].StructValue;
        Assert.Equal("memory_queried", slots.Fields["status"].StringValue);
    }

    [Fact]
    public async Task StepAsync_returns_goal_reached_terminal_status_when_intention_status_becomes_file_opened()
    {
        var intention = new IntentionModule();
        SeedGoal(intention);

        var operation = new BufferOperation
        {
            TargetModuleId = "intention",
            Command = "modify_slot",
            Params = new Struct
            {
                Fields =
                {
                    ["slot"] = Value.ForString("status"),
                    ["slot_value"] = Value.ForString("file_opened")
                }
            }
        };

        var procedural = new FakeProceduralMemory
        {
            Conditions = [new ProceduralCondition { RuleId = "rule-memory-hit" }],
            SelectedAction = new NeuroAction { RuleId = "rule-memory-hit" }
        };
        var neuro = new FakeNeuroCore
        {
            SatisfiedRuleIds = ["rule-memory-hit"],
            Operations = [operation]
        };
        var core = CreateCore(procedural, neuro, intention);

        var result = await core.StepAsync();

        Assert.Equal(StepStopReason.GoalReachedTerminalStatus, result.StopReason);
        Assert.True(result.IsTerminal);
        Assert.Equal("rule-memory-hit", result.SelectedRuleId);
    }

    private static HarnessCore CreateCore(
        FakeProceduralMemory procedural,
        FakeNeuroCore neuro,
        IntentionModule intention)
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(intention);
        return new HarnessCore(registry, procedural, neuro);
    }

    private static void SeedGoal(IntentionModule intention)
    {
        intention.OperateBuffer(new BufferOperation
        {
            TargetModuleId = "intention",
            Command = "set_goal",
            Params = new SetGoalRequest(
                "CodeSearch",
                new Struct
                {
                    Fields =
                    {
                        ["query"] = Value.ForString("find file explorer code"),
                        ["status"] = Value.ForString("start")
                    }
                }).ToStruct()
        });
    }

    private sealed class FakeProceduralMemory : IProceduralMemory
    {
        public IReadOnlyList<ProceduralCondition> Conditions { get; init; } = [];
        public NeuroAction SelectedAction { get; init; } = new();
        public List<IReadOnlyList<string>> SelectionInputs { get; } = [];

        public IReadOnlyList<ProceduralCondition> GetAllConditions()
        {
            return Conditions;
        }

        public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
        {
            SelectionInputs.Add(satisfiedRuleIds);
            return SelectedAction;
        }
    }

    private sealed class FakeNeuroCore : INeuroCore
    {
        public IReadOnlyList<string> SatisfiedRuleIds { get; init; } = [];
        public IReadOnlyList<BufferOperation> Operations { get; init; } = [];

        public Task<IReadOnlyList<string>> EvaluateConditionsAsync(
            IReadOnlyList<ProceduralCondition> conditions,
            IReadOnlyList<BufferState> bufferStates,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SatisfiedRuleIds);
        }

        public Task<IReadOnlyList<BufferOperation>> DecodeActionAsync(
            NeuroAction actionIntent,
            IReadOnlyList<BufferState> currentStates,
            IReadOnlyList<ModuleSchema> schemas,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Operations);
        }
    }
}
