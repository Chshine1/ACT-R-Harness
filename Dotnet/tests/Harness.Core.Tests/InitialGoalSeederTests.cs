using Harness.Core.Modules;
using Harness.Host;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Core.Tests;

public class InitialGoalSeederTests
{
    [Fact]
    public void Seed_clears_existing_goals_and_sets_the_configured_root_goal()
    {
        var intention = new IntentionModule();
        SeedExistingGoal(intention);

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
        Assert.Equal(1, state.Fields["stack_depth"].NumberValue);
    }

    private static void SeedExistingGoal(IntentionModule intention)
    {
        intention.OperateBuffer(new Harness.Abstractions.Actr.BufferOperation
        {
            TargetModuleId = "intention",
            Command = "set_goal",
            Params = new SetGoalRequest(
                "OldGoal",
                new Google.Protobuf.WellKnownTypes.Struct
                {
                    Fields =
                    {
                        ["query"] = Google.Protobuf.WellKnownTypes.Value.ForString("stale"),
                        ["status"] = Google.Protobuf.WellKnownTypes.Value.ForString("done")
                    }
                }).ToStruct()
        });
    }
}
