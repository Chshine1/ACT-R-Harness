using Actr;

namespace Harness.Core;

public interface IProceduralMemory
{
    IReadOnlyList<ProceduralCondition> GetAllConditions();
    NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds);
    Task LearnUtilityAsync(string ruleId, float reward, CancellationToken cancellation = default);
}