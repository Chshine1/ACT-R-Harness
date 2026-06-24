using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions.Actr;

namespace Harness.Core.Modules;

public partial class IntentionModule
{
    private const int MaxStackSize = 7;

    private readonly Stack<Goal> _goalStack = new();
    private string? _lastExposedGoalId;

    public void OperateBuffer(BufferOperation op)
    {
        switch (op.Command)
        {
            case "setGoal":
                SetGoal(op.Params);
                break;
            case "pushSubgoal":
                PushSubgoal(op.Params);
                break;
            case "popGoal":
                PopGoal();
                break;
            case "clearGoals":
                ClearGoals();
                break;
            case "modifySlot":
                ModifySlot(op.Params);
                break;
            default:
                throw new InvalidOperationException(
                    $"IntentionModule does not support command '{op.Command}'.");
        }
    }

    private static double Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    private void SetGoal(Struct parameters)
    {
        var goal = Goal.FromStruct(parameters);
        goal.CreationTime = Now();

        if (_goalStack.Count > 0)
            _goalStack.Pop();
        _goalStack.Push(goal);
    }

    private void PushSubgoal(Struct parameters)
    {
        var goal = Goal.FromStruct(parameters);
        goal.CreationTime = Now();

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

    private void PopGoal()
    {
        if (_goalStack.Count > 0)
            _goalStack.Pop();
    }

    private void ClearGoals()
    {
        _goalStack.Clear();
    }

    private void ModifySlot(Struct parameters)
    {
        if (!_goalStack.TryPeek(out var goal))
            throw new InvalidOperationException("No current goal to modify.");

        if (!parameters.Fields.TryGetValue("slot", out var slotField) ||
            slotField.KindCase != Value.KindOneofCase.StringValue)
            throw new ArgumentException("'slot' must be a string.");

        if (!parameters.Fields.TryGetValue("value", out var valueField))
            throw new ArgumentException("'value' is required.");

        goal.Slots[slotField.StringValue] = valueField;
    }

    private class Goal
    {
        private string Id { get; init; } = Guid.NewGuid().ToString();
        public double CreationTime { get; set; }
        public Dictionary<string, Value> Slots { get; } = new();

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

        public static Goal FromStruct(Struct s)
        {
            var goal = new Goal
            {
                Id = s.Fields.TryGetValue("id", out var idVal) && idVal.KindCase == Value.KindOneofCase.StringValue
                    ? idVal.StringValue
                    : Guid.NewGuid().ToString(),
                CreationTime = s.Fields.TryGetValue("creation_time", out var ctVal) &&
                               ctVal.KindCase == Value.KindOneofCase.NumberValue
                    ? ctVal.NumberValue
                    : 0.0
            };

            if (!s.Fields.TryGetValue("slots", out var slotsVal) ||
                slotsVal.KindCase != Value.KindOneofCase.StructValue) return goal;
            foreach (var field in slotsVal.StructValue.Fields)
                goal.Slots[field.Key] = field.Value;

            return goal;
        }
    }
}