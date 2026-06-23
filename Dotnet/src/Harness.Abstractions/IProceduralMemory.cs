using Harness.Abstractions.Actr;

namespace Harness.Abstractions;

public interface IProceduralMemory
{
    IReadOnlyList<ProceduralCondition> GetAllConditions();
    NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds);
}