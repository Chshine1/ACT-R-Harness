using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;

namespace Harness.Core.Modules;

public partial class IntentionModule : IModule
{
    public string ModuleId => "Intention";

    public BufferState GetBufferState()
    {
        var data = new Struct();

        if (_goalStack.TryPeek(out var currentGoal))
        {
            data.Fields["current_goal"] = Value.ForStruct(currentGoal.ToStruct());
            data.Fields["stack_depth"] = Value.ForNumber(_goalStack.Count);
        }
        else
        {
            data.Fields["current_goal"] = Value.ForNull();
            data.Fields["stack_depth"] = Value.ForNumber(0);
        }

        data.Fields["max_capacity"] = Value.ForNumber(MaxStackSize);

        return new BufferState
        {
            ModuleId = ModuleId,
            Data = data
        };
    }

    public ModuleSchema GetOperationSchema()
    {
        return new ModuleSchema
        {
            ModuleId = ModuleId,
            CommandSchemas =
            {
                ["setGoal"] =
                    """
                    {
                        "id": "string",
                        "slots": { "type": "object" }
                    }
                    """,
                ["pushSubgoal"] =
                    """
                    {
                        "id": "string",
                        "slots": { "type": "object" }
                    }
                    """,
                ["popGoal"] = "{}",
                ["clearGoals"] = "{}",
                ["modifySlot"] =
                    """
                    {
                        "slot": "string",
                        "value": "any"
                    }
                    """
            }
        };
    }
}