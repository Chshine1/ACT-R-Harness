using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Core.Modules;
using Harness.Host.Options;
using Microsoft.Extensions.Options;

namespace Harness.Host;

public class InitialGoalSeeder(IntentionModule intentionModule, IOptions<HarnessOptions> options)
{
    private readonly InitialGoalOptions _initialGoal = options.Value.InitialGoal;

    public void Seed()
    {
        intentionModule.OperateBuffer(new BufferOperation
        {
            TargetModuleId = intentionModule.ModuleId,
            Command = "clear_goals",
            Params = new Struct()
        });

        intentionModule.OperateBuffer(new BufferOperation
        {
            TargetModuleId = intentionModule.ModuleId,
            Command = "set_goal",
            Params = new SetGoalRequest(
                _initialGoal.Id,
                new Struct
                {
                    Fields =
                    {
                        ["query"] = Value.ForString(_initialGoal.Query),
                        ["status"] = Value.ForString("start")
                    }
                }).ToStruct()
        });
    }
}
