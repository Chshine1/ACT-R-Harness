using Harness.Abstractions;
using Harness.Abstractions.Actr;

namespace Harness.Core;

public class ProceduralMemory : IProceduralMemory
{
    public IReadOnlyList<ProceduralCondition> GetAllConditions()
    {
        throw new NotImplementedException();
    }

    public NeuroAction SelectRule(IReadOnlyList<string> satisfiedRuleIds)
    {
        throw new NotImplementedException();
    }

    public Task LearnUtilityAsync(string ruleId, float reward, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }
}