using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;
using Harness.Abstractions.Reward;
using Harness.Core;
using Harness.Core.Modules;
using Harness.Host;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Core.Tests;

public class HarnessEpochExecutorTests
{
    [Fact]
    public async Task RunAsync_seeds_the_root_goal_and_does_not_tick_when_no_rule_is_selected()
    {
        var intention = new IntentionModule();
        var core = CreateCore(
            intention,
            new FakeProceduralMemory(),
            new FakeNeuroCore { SatisfiedRuleIds = [] });
        var reward = new FakeRewardService();
        var clock = new FakeClock();
        var executor = CreateExecutor(intention, core, reward, clock, maxSteps: 5);

        var result = await executor.RunAsync(CancellationToken.None);

        Assert.Equal(EpochStopReason.NoApplicableRule, result.StopReason);
        Assert.Equal(0, reward.CallCount);
        Assert.Equal(0, clock.TickCount);

        var goal = intention.GetBufferState().Data.Fields["current_goal"].StructValue;
        var slots = goal.Fields["slots"].StructValue;
        Assert.Equal("CodeSearch", goal.Fields["id"].StringValue);
        Assert.Equal("find file explorer code", slots.Fields["query"].StringValue);
        Assert.Equal("start", slots.Fields["status"].StringValue);
    }

    [Fact]
    public async Task RunAsync_ticks_once_when_a_selected_rule_reaches_a_terminal_goal_status()
    {
        var intention = new IntentionModule();
        var core = CreateCore(
            intention,
            new FakeProceduralMemory
            {
                Conditions = [new ProceduralCondition { RuleId = "rule-memory-hit" }],
                SelectedAction = new NeuroAction { RuleId = "rule-memory-hit" }
            },
            new FakeNeuroCore
            {
                SatisfiedRuleIds = ["rule-memory-hit"],
                Operations =
                [
                    new BufferOperation
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
                    }
                ]
            });
        var reward = new FakeRewardService();
        var clock = new FakeClock();
        var executor = CreateExecutor(intention, core, reward, clock, maxSteps: 5);

        var result = await executor.RunAsync(CancellationToken.None);

        Assert.Equal(EpochStopReason.GoalReachedTerminalStatus, result.StopReason);
        Assert.Equal(1, reward.CallCount);
        Assert.Equal(1, clock.TickCount);
    }

    [Fact]
    public async Task RunAsync_stops_at_the_configured_max_steps_for_non_terminal_rules()
    {
        var intention = new IntentionModule();
        var core = CreateCore(
            intention,
            new FakeProceduralMemory
            {
                Conditions = [new ProceduralCondition { RuleId = "rule-init" }],
                SelectedAction = new NeuroAction { RuleId = "rule-init" }
            },
            new FakeNeuroCore
            {
                SatisfiedRuleIds = ["rule-init"],
                Operations = []
            });
        var reward = new FakeRewardService();
        var clock = new FakeClock();
        var executor = CreateExecutor(intention, core, reward, clock, maxSteps: 2);

        var result = await executor.RunAsync(CancellationToken.None);

        Assert.Equal(EpochStopReason.MaxStepsReached, result.StopReason);
        Assert.Equal(2, result.StepsExecuted);
        Assert.Equal(2, reward.CallCount);
        Assert.Equal(2, clock.TickCount);
    }

    private static HarnessCore CreateCore(
        IntentionModule intention,
        FakeProceduralMemory procedural,
        FakeNeuroCore neuro)
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(intention);
        return new HarnessCore(registry, procedural, neuro);
    }

    private static HarnessEpochExecutor CreateExecutor(
        IntentionModule intention,
        HarnessCore core,
        FakeRewardService reward,
        FakeClock clock,
        int maxSteps)
    {
        var options = Options.Create(new HarnessOptions
        {
            Training = true,
            MaxEpochs = 1,
            MaxStepsPerEpoch = maxSteps,
            InitialGoal = new InitialGoalOptions
            {
                Id = "CodeSearch",
                Query = "find file explorer code"
            }
        });

        return new HarnessEpochExecutor(
            core,
            new InitialGoalSeeder(intention, options),
            clock,
            reward,
            options);
    }

    private sealed class FakeProceduralMemory : IProceduralMemory
    {
        public IReadOnlyList<ProceduralCondition> Conditions { get; init; } = [];
        public NeuroAction SelectedAction { get; init; } = new();

        public IReadOnlyList<ProceduralCondition> GetAllConditions()
        {
            return Conditions;
        }

        public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
        {
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

    private sealed class FakeRewardService : IRewardService
    {
        public int CallCount { get; private set; }

        public Task<float> ComputeRewardAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(0.5f);
        }
    }

    private sealed class FakeClock : IClock
    {
        public int TickCount { get; private set; }

        public event IClock.AsyncEventHandler<StepState>? OnTickAsync;

        public Task TickAsync(StepState stepState, CancellationToken cancellationToken)
        {
            TickCount++;
            return OnTickAsync?.Invoke(stepState, cancellationToken) ?? Task.CompletedTask;
        }
    }
}
