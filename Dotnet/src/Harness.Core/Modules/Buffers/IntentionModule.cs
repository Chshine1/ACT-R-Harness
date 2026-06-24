using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Core.Modules;

public partial class IntentionModule : IModule
{
    public string ModuleId => "Intention";

    public BufferState GetBufferState()
    {
        var data = new Struct();

        string? currentGoalId = null;
        if (_goalStack.TryPeek(out var currentGoal))
        {
            currentGoalId = currentGoal.ToStruct().Fields["id"].StringValue;
            data.Fields["current_goal"] = Value.ForStruct(currentGoal.ToStruct());
            data.Fields["stack_depth"] = Value.ForNumber(_goalStack.Count);
        }
        else
        {
            data.Fields["current_goal"] = Value.ForNull();
            data.Fields["stack_depth"] = Value.ForNumber(0);
        }
        
        var goalJustChanged = currentGoalId != _lastExposedGoalId;
        data.Fields["goal_just_changed"] = Value.ForBool(goalJustChanged);
        data.Fields["last_goal_id"] = _lastExposedGoalId != null
            ? Value.ForString(_lastExposedGoalId)
            : Value.ForNull();

        _lastExposedGoalId = currentGoalId;

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