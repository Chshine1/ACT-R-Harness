using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Modules;

namespace Harness.Core.Modules;

[ModuleCommandRequest(
    """
    {
        "id": "string",
        "slots": { "type": "object" }
    }
    """)]
public record SetGoalRequest(string Id, Struct Slots) : IStructRepresentable<SetGoalRequest>
{
    public Struct ToStruct() => new() { Fields = { ["id"] = Value.ForString(Id), ["slots"] = Value.ForStruct(Slots) } };
    public static SetGoalRequest FromStruct(Struct s) => new(s.Fields["id"].StringValue, s.Fields["slots"].StructValue);
}

[ModuleCommandRequest(
    """
    {
        "id": "string",
        "slots": { "type": "object" }
    }
    """)]
public record PushSubgoalRequest(string Id, Struct Slots) : IStructRepresentable<PushSubgoalRequest>
{
    public Struct ToStruct() => new() { Fields = { ["id"] = Value.ForString(Id), ["slots"] = Value.ForStruct(Slots) } };
    public static PushSubgoalRequest FromStruct(Struct s) => new(s.Fields["id"].StringValue, s.Fields["slots"].StructValue);
}

[ModuleCommandRequest(
    """
    {
        "slot": "string",
        "slot_value": "object"
    }
    """)]
public record ModifySlotRequest(string Slot, Struct SlotValue) : IStructRepresentable<ModifySlotRequest>
{
    public Struct ToStruct() => new() { Fields = { ["slot"] = Value.ForString(Slot), ["slot_value"] = Value.ForStruct(SlotValue) } };
    public static ModifySlotRequest FromStruct(Struct s) => new(s.Fields["slot"].StringValue, s.Fields["slot_value"].StructValue);
}

public class IntentionModule : ModuleBase
{
    private const int MaxStackSize = 7;

    private readonly Stack<Goal> _goalStack = new();
    private string? _lastExposedGoalId;
    
    public override string ModuleId => "intention";

    public override BufferState GetBufferState()
    {
        var data = new Struct();

        string? currentGoalId = null;
        if (_goalStack.TryPeek(out var currentGoal))
        {
            currentGoalId = currentGoal.Id;
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

    [ModuleCommand("set_goal")]
    protected void SetGoal(SetGoalRequest request)
    {
        var goal = new Goal
        {
            Id = request.Id,
            CreationTime = Now(),
            Slots = request.Slots.Fields.ToDictionary()
        };

        if (_goalStack.Count > 0)
            _goalStack.Pop();
        _goalStack.Push(goal);
    }
    
    [ModuleCommand("push_subgoal")]
    protected void PushSubgoal(PushSubgoalRequest request)
    {
        var goal = new Goal
        {
            Id = request.Id,
            CreationTime = Now(),
            Slots = request.Slots.Fields.ToDictionary()
        };

        while (_goalStack.Count >= MaxStackSize)
        {
            var temp = new List<Goal>(_goalStack);
            temp.RemoveAt(temp.Count - 1);
            _goalStack.Clear();
            for (var i = temp.Count - 1; i >= 0; i--)
                _goalStack.Push(temp[i]);
        }

        _goalStack.Push(goal);
    }

    [ModuleCommand("pop_goal")]
    protected void PopGoal()
    {
        if (_goalStack.Count > 0)
            _goalStack.Pop();
    }

    [ModuleCommand("clear_goals")]
    protected void ClearGoals()
    {
        _goalStack.Clear();
    }

    [ModuleCommand("modify_slot")]
    protected void ModifySlot(ModifySlotRequest request)
    {
        if (!_goalStack.TryPeek(out var goal))
            throw new InvalidOperationException("No current goal to modify.");

        goal.Slots[request.Slot] = Value.ForStruct(request.SlotValue);
    }

    private class Goal
    {
        public required string Id { get; init; }
        public required double CreationTime { get; init; }
        public required Dictionary<string, Value> Slots { get; init; }
        
        public Struct ToStruct()
        {
            var s = new Struct
            {
                Fields =
                {
                    ["id"] = Value.ForString(Id),
                    ["creation_time"] = Value.ForNumber(CreationTime)
                }
            };
            var slotsStruct = new Struct();
            foreach (var kv in Slots)
                slotsStruct.Fields[kv.Key] = kv.Value;
            s.Fields["slots"] = Value.ForStruct(slotsStruct);
            return s;
        }
    }

    private static double Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}