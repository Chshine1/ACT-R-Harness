namespace Harness.Core.NeuroCore;

public class SymbolicMatcher
{
    public bool Evaluate(IReadOnlyDictionary<string, object?> conditionNode, BuffersView view)
    {
        if (!conditionNode.TryGetValue("type", out var typeValue) || typeValue is not string type)
        {
            return false;
        }

        return type switch
        {
            "and" => EnumerateConditions(conditionNode, "conditions").All(condition => Evaluate(condition, view)),
            "or" => EnumerateConditions(conditionNode, "conditions").Any(condition => Evaluate(condition, view)),
            "not" => conditionNode.TryGetValue("condition", out var nested)
                     && nested is IReadOnlyDictionary<string, object?> nestedMap
                     && !Evaluate(nestedMap, view),
            "equals" => conditionNode.TryGetValue("slot", out var slotValue)
                        && slotValue is string slot
                        && conditionNode.TryGetValue("value", out var expectedValue)
                        && Equals(view.Get(slot), expectedValue),
            "exist" => conditionNode.TryGetValue("slot", out var existsSlotValue)
                       && existsSlotValue is string existsSlot
                       && view.Get(existsSlot) is not null,
            _ => false
        };
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> EnumerateConditions(
        IReadOnlyDictionary<string, object?> node,
        string key)
    {
        if (!node.TryGetValue(key, out var value) || value is not IEnumerable<object?> items)
        {
            return [];
        }

        return items.OfType<IReadOnlyDictionary<string, object?>>();
    }
}
